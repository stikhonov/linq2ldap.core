using System;
using System.Linq;
using System.Reflection;
using Linq2Ldap.Core.Attributes;
using Linq2Ldap.Core.Models;
using Linq2Ldap.Core.Proxies;
using Linq2Ldap.Core.Types;
using Linq2Ldap.Core.Util;

namespace Linq2Ldap.Core {
    public class ModelCreator: IModelCreator {
        /// <summary>
        /// Creates a new instance of an LDAP Entry model and populates its fields.
        /// </summary>
        /// <param name="entryProps">A proxy dictionary of the LDAP entry's fields.</param>
        /// <param name="dn">DistinguishedName field of this entry.</param>
        /// <typeparam name="T">The destination IEntry model type.</typeparam>
        /// <returns>A populated instance of T.</returns>
        public T Create<T>(EntryAttributeDictionary entryProps, string dn = null)
            where T: IEntry, new()
            => Create(new T() {
                    DistinguishedName = dn,
                    Attributes = entryProps
                }, entryProps);

        /// <summary>
        /// Given an IEntry class, T, populate its LdapField properties from the given
        /// collection of directory entry attributes.
        /// </summary>
        /// <typeparam name="T">A type implementing IEntry.</typeparam>
        /// <param name="model">An object whose type implements IEntry.</param>
        /// <param name="entryProps">A collection of directory entry properties and values.</param>
        /// <returns></returns>
        public virtual T Create<T>(
            T model,
            EntryAttributeDictionary entryProps
        )
            where T: IEntry
        {
            var t = typeof(T);
            var props = t.GetProperties();
            foreach (var prop in props)
            {
                SetPropFromProperties(model, prop, entryProps);
            }

            return model;
        }

        protected virtual void SetPropFromProperties<T>(
            T model,
            PropertyInfo prop,
            EntryAttributeDictionary entryProps
        )
            where T: IEntry
        {
            var attr = prop.GetCustomAttribute<LdapFieldAttribute>();
            if (attr == null)
            {
                return;
            }

            var ldapName = attr?.Name ?? prop.Name;
            AttributeValueList val;
            if (! entryProps.ContainsKey(ldapName)) {
                if (! attr.Optional) {
                    throw new ArgumentException(
                        $"Column attribute {attr.Name} is not marked as optional, but was not"
                        + " found in the store.");
                }

                val = null;
            } else {
                val = entryProps[ldapName];
            }

            if (prop.CanWrite)
            {
                ValidateTypeConvertAndSet(model, val, prop, ldapName);
            }
            else
            {
                throw new ArgumentException(
                    $"Column attribute {attr.Name} applied to property {prop.Name}, "
                    + $"but {prop.Name} is not writable.");
            }
        }

        protected void ValidateTypeConvertAndSet<T>(
            T model,
            AttributeValueList ldapFieldData,
            PropertyInfo prop,
            string ldapName
        )
            where T: IEntry
        {
            var ptype = prop.PropertyType;
            Type[] genArgs = ptype.BaseType
                .GetGenericArguments()
                .Where(a => !a.IsGenericParameter)
                .ToArray(); ;
            Type[] baseLdapTypes = new[] {
                typeof(BaseLdapManyType<,>),
                typeof(BaseLdapType<,>) };
            if (ptype.BaseType.IsGenericType
                && baseLdapTypes.Any(t =>
                    genArgs.Count() == t.GetGenericArguments().Count()
                    && TypesUtility.CanMakeGenericTypeAssignableFrom(t, genArgs, ptype)))
            {
                if (ldapFieldData == null) {
                    prop.SetValue(model, null);
                    return;
                }

                var converter = Activator.CreateInstance(genArgs[1]);
                var inst = Activator.CreateInstance(ptype, ldapFieldData, converter);
                prop.SetValue(model, inst);
                return;
            }

            if (ldapFieldData == null || ldapFieldData.Count == 0) {
                prop.SetValue(model, null);
                return;
            }
            else if (ldapFieldData.Count == 1)
            {
                prop.SetValue(model, ldapFieldData[0]);
                return;
            }

            throw new FormatException(
                $"Mapping to non-array type, but LDAP data is array: {ldapName} -> {prop.Name}.");
        }
    }
}