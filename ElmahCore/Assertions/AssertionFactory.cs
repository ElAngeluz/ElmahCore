#region License, Terms and Author(s)
//
// ELMAH - Error Logging Modules and Handlers for ASP.NET
// Copyright (c) 2004-9 Atif Aziz. All rights reserved.
//
//  Author(s):
//
//      Atif Aziz, http://www.raboof.com
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
#endregion

//[assembly: Elmah.Scc("$Id: AssertionFactory.cs 640 2009-06-01 17:22:02Z azizatif $")]

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;

namespace ElmahCore.Assertions
{
    #region Imports

	#endregion

    /// <summary>
    /// Represents the method that will be responsible for creating an 
    /// assertion object and initializing it from an XML configuration
    /// element.
    /// </summary>

    internal delegate IAssertion AssertionFactoryHandler(XmlElement config);

    /// <summary>
    /// Holds factory methods for creating configured assertion objects.
    /// </summary>

    internal static class AssertionFactory
    {
        public static IAssertion Assert_is_null(IContextExpression binding) => new IsNullAssertion(binding);

        public static IAssertion Assert_is_not_null(IContextExpression binding) => new UnaryNotAssertion(Assert_is_null(binding));

        public static IAssertion Assert_equal(IContextExpression binding, TypeCode type, string value) => new ComparisonAssertion(ComparisonResults.Equal, binding, type, value);

        public static IAssertion Assert_not_equal(IContextExpression binding, TypeCode type, string value) => new UnaryNotAssertion(Assert_equal(binding, type, value));

        public static IAssertion Assert_lesser(IContextExpression binding, TypeCode type, string value) => new ComparisonAssertion(ComparisonResults.Lesser, binding, type, value);

        public static IAssertion Assert_lesser_or_equal(IContextExpression binding, TypeCode type, string value) => new ComparisonAssertion(ComparisonResults.LesserOrEqual, binding, type, value);

        public static IAssertion Assert_greater(IContextExpression binding, TypeCode type, string value) => new ComparisonAssertion(ComparisonResults.Greater, binding, type, value);

        public static IAssertion Assert_greater_or_equal(IContextExpression binding, TypeCode type, string value) => new ComparisonAssertion(ComparisonResults.GreaterOrEqual, binding, type, value);

        public static IAssertion Assert_and(XmlElement config) => LogicalAssertion.LogicalAnd(Create(config.ChildNodes));

        public static IAssertion Assert_or(XmlElement config) => LogicalAssertion.LogicalOr(Create(config.ChildNodes));

        public static IAssertion Assert_not(XmlElement config) => LogicalAssertion.LogicalNot(Create(config.ChildNodes));

        public static IAssertion Assert_is_type(IContextExpression binding, Type type) => new TypeAssertion(binding, type, /* byCompatibility */ false);

        public static IAssertion Assert_is_type_compatible(IContextExpression binding, Type type) => new TypeAssertion(binding, type, /* byCompatibility */ true);

        public static IAssertion Assert_regex(IContextExpression binding, string pattern, bool caseSensitive, bool dontCompile)
        {
            if ((pattern ?? string.Empty).Length == 0)
                return StaticAssertion.False;

            //
            // NOTE: There is an assumption here that most uses of this
            // assertion will be for culture-insensitive matches. Since
            // it is difficult to imagine all the implications of involving
            // a culture at this point, it seems safer to just err with the
            // invariant culture settings.
            //

            var options = RegexOptions.CultureInvariant;

            if (!caseSensitive)
                options |= RegexOptions.IgnoreCase;

            if (!dontCompile)
                options |= RegexOptions.Compiled;

            // ReSharper disable once AssignNullToNotNullAttribute
            return new RegexMatchAssertion(binding, new Regex(pattern, options));
        }


        public static IAssertion Create(XmlElement config)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            try
            {
                return CreateImpl(config);
            }
            catch (Exception e)
            {
                throw new Exception(e.Message, e);
            }
        }

        public static IAssertion[] Create(XmlNodeList nodes)
        {
            if (nodes == null) 
                throw new ArgumentNullException("nodes");

            //
            // First count the number of elements, which will be used to
            // allocate the array at its correct and final size.
            //

            var elementCount = 0;

            foreach (XmlNode child in nodes)
            {
                var nodeType = child.NodeType;

                //
                // Skip comments and whitespaces.
                //

                if (nodeType == XmlNodeType.Comment || nodeType == XmlNodeType.Whitespace)
                    continue;

                //
                // Otherwise all elements only.
                //

                if (nodeType != XmlNodeType.Element)
                {
                    throw new Exception(
                        string.Format("Unexpected type of node ({0}).", nodeType.ToString()));
                }

                elementCount++;
            }

            //
            // In the second pass, create and configure the assertions
            // from each element.
            //

            var assertions = new IAssertion[elementCount];
            elementCount = 0;

            foreach (XmlNode node in nodes)
            {
                if (node.NodeType == XmlNodeType.Element)
                    assertions[elementCount++] = Create((XmlElement) node);
            }

            return assertions;
        }

        private static IAssertion CreateImpl(XmlElement config)
        {
            Debug.Assert(config != null);

            var name = "assert_" + config.LocalName;
            
            if (name.IndexOf('-') > 0)
                name = name.Replace("-", "_");
            
            Type factoryType;

            var xmlns = config.NamespaceURI;

            if (xmlns.Length > 0)
            {
                if (!DecodeClrTypeNamespaceFromXmlNamespace(xmlns, out string ns, out string assemblyName)
                    || ns.Length == 0 || assemblyName.Length == 0)
                {
                    throw new Exception(string.Format(
                        "Error decoding CLR type namespace and assembly from the XML namespace '{0}'.", xmlns));
                }

                var assembly = Assembly.Load(assemblyName);
                factoryType = assembly.GetType(ns + ".AssertionFactory", /* throwOnError */ true);
            }
            else
            {
                factoryType = typeof(AssertionFactory);
            }

            var method = factoryType.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                throw new MissingMemberException(string.Format(
                    "{0} does not have a method named {1}. " +
                    "Ensure that the method is named correctly and that it is public and static.",
                    factoryType, name));
            }

            var parameters = method.GetParameters();

            if (parameters.Length == 1 
                && parameters[0].ParameterType == typeof(XmlElement)
                && method.ReturnType == typeof(IAssertion))
            {
                var handler = (AssertionFactoryHandler) Delegate.CreateDelegate(typeof(AssertionFactoryHandler), factoryType, name);
                return handler(config); // TODO Check if Delegate.CreateDelegate could return null
            }

            return (IAssertion) method.Invoke(null, ParseArguments(method, config));
        }

        private static object[] ParseArguments(MethodInfo method, XmlElement config) 
        {
            Debug.Assert(method != null);
            Debug.Assert(config != null);

            var parameters = method.GetParameters();
            var args = new object[parameters.Length];
            
            foreach (var parameter in parameters)
                args[parameter.Position] = ParseArgument(parameter, config);

            return args;
        }

        private static readonly string[] Truths = new[] { "true", "yes", "on", "1" }; // TODO Remove duplication with SecurityConfiguration
        
        private static object ParseArgument(ParameterInfo parameter, XmlElement config) 
        {
            Debug.Assert(parameter != null);
            Debug.Assert(config != null);

            var name = parameter.Name;
            var type = parameter.ParameterType;
            string text;

            var attribute = config.GetAttributeNode(name);
            if (attribute != null)
            {
                text = attribute.Value;
            }
            else
            {
                var element = config[name];
                if (element == null)
                    return null;

                text = element.InnerText;
            }

            if (type == typeof(IContextExpression))
                return new WebDataBindingExpression(text);

            if (type == typeof(Type))
                return TypeResolution.GetType(text);

            if (type == typeof(bool))
            {
                text = text.Trim().ToLowerInvariant();
                return bool.TrueString.Equals(Array.IndexOf(Truths, text) >= 0 ? bool.TrueString : text);

	            
            }

            var converter = TypeDescriptor.GetConverter(type);
            return converter.ConvertFromInvariantString(text);
        }

        /// <remarks>
        /// Ideally, we would be able to use SoapServices.DecodeXmlNamespaceForClrTypeNamespace
        /// but that requires a link demand permission that will fail in partially trusted
        /// environments such as ASP.NET medium trust.
        /// </remarks>
        
        private static bool DecodeClrTypeNamespaceFromXmlNamespace(string xmlns, out string typeNamespace, out string assemblyName)
        {
            Debug.Assert(xmlns != null);

            assemblyName = string.Empty;
            typeNamespace = string.Empty;

            const string assemblyNs = "http://schemas.microsoft.com/clr/assem/";
            const string namespaceNs = "http://schemas.microsoft.com/clr/ns/";
            const string fullNs = "http://schemas.microsoft.com/clr/nsassem/";
            
            if (OrdinalStringStartsWith(xmlns, assemblyNs))
            {
                assemblyName = Uri.UnescapeDataString(xmlns.Substring(assemblyNs.Length));
                return assemblyName.Length > 0;
            }
            
            if (OrdinalStringStartsWith(xmlns, namespaceNs))
            {
                typeNamespace = xmlns.Substring(namespaceNs.Length);
                return typeNamespace.Length > 0;
            }
            
            if (OrdinalStringStartsWith(xmlns, fullNs))
            {
                var index = xmlns.IndexOf("/", fullNs.Length, StringComparison.InvariantCultureIgnoreCase);
                typeNamespace = xmlns.Substring(fullNs.Length, index - fullNs.Length);
                assemblyName = Uri.UnescapeDataString(xmlns.Substring(index + 1));
                return assemblyName.Length > 0 && typeNamespace.Length > 0;
            }

            return false;
        }
        
        private static bool OrdinalStringStartsWith(string s, string prefix)
        {
            Debug.Assert(s != null);
            Debug.Assert(prefix != null);
            
            return s.Length >= prefix.Length && 
                string.CompareOrdinal(s.Substring(0, prefix.Length), prefix) == 0;
        }
    }
}
