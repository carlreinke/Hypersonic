//
// Copyright (C) 2018  Carl Reinke
//
// This file is part of Hypersonic.
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without
// even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// General Public License for more details.
//
// You should have received a copy of the GNU General Public License along with this program.  If
// not, see <https://www.gnu.org/licenses/>.
//
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;

namespace Hypersonic
{
    internal sealed class XmlSerializationContractResolver : DefaultContractResolver
    {
        public static readonly XmlSerializationContractResolver Instance = new XmlSerializationContractResolver();

        private XmlSerializationContractResolver()
        {
            IgnoreIsSpecifiedMembers = true;
            IgnoreSerializableAttribute = true;
            IgnoreSerializableInterface = true;
            IgnoreShouldSerializeMembers = true;
        }

        protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
        {
            if (memberSerialization != MemberSerialization.OptOut)
                throw new ArgumentException("Only opt-out is supported.", nameof(memberSerialization));

            if (type.GetCustomAttribute<XmlTypeAttribute>() == null)
                throw new InvalidOperationException();

            var jsonProperties = new List<JsonProperty>();

            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var property in properties)
            {
                if (property.GetCustomAttribute<XmlIgnoreAttribute>() != null)
                    continue;

                var choiceAttribute = property.GetCustomAttribute<XmlChoiceIdentifierAttribute>();
                var attributeAttributes = property.GetCustomAttributes<XmlAttributeAttribute>().ToArray();
                var elementAttributes = property.GetCustomAttributes<XmlElementAttribute>().ToArray();
                var textAttribute = property.GetCustomAttribute<XmlTextAttribute>();

                PropertyInfo choiceProperty = null;

                if (choiceAttribute == null)
                {
                    if (attributeAttributes.Length + elementAttributes.Length > 1)
                        throw new InvalidOperationException();
                }
                else
                {
                    choiceProperty = type.GetProperty(choiceAttribute.MemberName);
                    if (choiceProperty == null)
                        throw new InvalidOperationException();
                    if (!choiceProperty.PropertyType.IsEnum)
                        throw new InvalidOperationException();
                }

                var names = new HashSet<string>();

                foreach (var attributeAttribute in attributeAttributes)
                {
                    var jsonProperty = CreateXmlProperty(property, memberSerialization, attributeAttribute.AttributeName, attributeAttribute.Type, choiceProperty);

                    if (names.Contains(jsonProperty.PropertyName))
                        throw new InvalidOperationException();
                    names.Add(jsonProperty.PropertyName);

                    jsonProperties.Add(jsonProperty);
                }

                foreach (var elementAttribute in elementAttributes)
                {
                    var jsonProperty = CreateXmlProperty(property, memberSerialization, elementAttribute.ElementName, elementAttribute.Type, choiceProperty);

                    if (names.Contains(jsonProperty.PropertyName))
                        throw new InvalidOperationException();
                    names.Add(jsonProperty.PropertyName);

                    jsonProperties.Add(jsonProperty);
                }

                if (textAttribute != null)
                {
                    var jsonProperty = CreateXmlTextProperty(property, memberSerialization);

                    jsonProperties.Add(jsonProperty);
                }
                else if (attributeAttributes.Length == 0 && elementAttributes.Length == 0)
                {
                    var jsonProperty = CreateXmlProperty(property, memberSerialization);

                    jsonProperties.Add(jsonProperty);
                }
            }

            return jsonProperties;
        }

        private JsonProperty CreateXmlProperty(PropertyInfo property, MemberSerialization memberSerialization, string name = null, Type type = null, PropertyInfo choiceProperty = null)
        {
            if (property.GetIndexParameters().Length > 0)
                throw new InvalidOperationException();

            var jsonProperty = CreateProperty(property, memberSerialization);

            if (!string.IsNullOrEmpty(name))
                jsonProperty.PropertyName = name;

            if (type != null)
                jsonProperty.PropertyType = type;

            if (choiceProperty != null)
            {
                jsonProperty.ShouldSerialize =
                    instance =>
                    {
                        if (property.GetValue(instance) == null)
                            return false;

                        object choiceValue = choiceProperty.GetValue(instance);
                        string choiceName = Enum.GetName(choiceProperty.PropertyType, choiceValue);
                        return choiceName == jsonProperty.PropertyName;
                    };
            }
            else
            {
                jsonProperty.ShouldSerialize =
                    instance => property.GetValue(instance) != null;
            }

            var specifiedProperty = property.DeclaringType.GetProperty(jsonProperty.PropertyName + "Specified", BindingFlags.Public | BindingFlags.Instance, null, typeof(bool), Type.EmptyTypes, null);
            if (specifiedProperty != null)
            {
                var specifiedPropertyIgnoreAttribute = specifiedProperty.GetCustomAttribute<XmlIgnoreAttribute>();
                if (specifiedPropertyIgnoreAttribute != null)
                {
                    jsonProperty.GetIsSpecified =
                        instance => (bool)specifiedProperty.GetValue(instance);
                }
            }

            return jsonProperty;
        }

        private JsonProperty CreateXmlTextProperty(PropertyInfo property, MemberSerialization memberSerialization)
        {
            if (property.GetIndexParameters().Length > 0)
                throw new InvalidOperationException();

            if (property.PropertyType != typeof(string[]))
                throw new InvalidOperationException();

            var jsonProperty = CreateProperty(property, memberSerialization);

            jsonProperty.PropertyName = "value";
            jsonProperty.PropertyType = typeof(string);
            jsonProperty.ValueProvider = new XmlTextPropertyValueProvider(property);

            return jsonProperty;
        }

        private class XmlTextPropertyValueProvider : IValueProvider
        {
            private readonly PropertyInfo _property;

            public XmlTextPropertyValueProvider(PropertyInfo property)
            {
                _property = property;
            }

            public object GetValue(object target)
            {
                var value = _property.GetValue(target);
                if (value == null)
                    return null;

                var strings = (string[])value;
                return string.Join(string.Empty, strings);
            }

            public void SetValue(object target, object value)
            {
                throw new NotImplementedException();
            }
        }
    }
}
