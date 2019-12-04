using System;
using System.Collections.Generic;
using System.Linq;
using AsyncDbg.Core;
using Microsoft.Diagnostics.Runtime;
#nullable enable

namespace AsyncDbg
{
    public class TypeIndex
    {
        private readonly Dictionary<ulong, ClrInstance> _instancesByAddress = new Dictionary<ulong, ClrInstance>();
        private readonly HashSet<ClrInstance> _instances = new HashSet<ClrInstance>(ClrInstanceAddressComparer.Instance);
        private readonly HashSet<ClrType> _derivedTypesAndRoot = new HashSet<ClrType>(ClrTypeEqualityComparer.Instance);

        public NodeKind Kind { get; }

        public ClrType? RootType { get; }

        public IReadOnlyCollection<ClrInstance> Instances => _instances;

        public TypeIndex(ClrType? rootType, NodeKind kind)
        {
            RootType = rootType;
            Kind = kind;

            if (rootType != null)
            {
                _derivedTypesAndRoot.Add(rootType);
            }
        }

        public bool AddIfDerived(ClrType type)
        {
            // exception when getting metadata reader for .winmd files
            if (type.Module.IsFile && type.Module.FileName.EndsWith(".winmd", System.StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (_derivedTypesAndRoot.Contains(type.BaseType)
                || RootType?.IsInterface == true && type.Interfaces.Any(i => i.Name == RootType.Name))
            {
                return _derivedTypesAndRoot.Add(type);
            }

            return false;
        }

        public void AddInstance(ClrInstance instance)
        {
            _instances.Add(instance);

            if (instance.ObjectAddress != null)
            {
                _instancesByAddress[instance.ObjectAddress.Value] = instance;
            }
        }

        public IEnumerable<ClrType> GetTypesByFullName(string fullName)
        {
            return _derivedTypesAndRoot.Where(type => type.Name == fullName).ToList();
        }

        public bool ContainsType(ClrType? type)
        {
            return type != null && _derivedTypesAndRoot.Contains(type);
        }

        public bool TryGetInstanceAt(ulong address, out ClrInstance result)
        {
            return _instancesByAddress.TryGetValue(address, out result);
        }
    }
}
