using System.Collections.Generic;
using System.Linq;
using Microsoft.Diagnostics.Runtime;

namespace AsyncCausalityDebuggerNew
{
    #nullable enable

    public class TypeIndex
    {
        private readonly HashSet<ClrInstance> _instances = new HashSet<ClrInstance>(ClrInstanceAddressComparer.Instance);
        private readonly HashSet<ClrType> _derivedTypesAndRoot = new HashSet<ClrType>(ClrTypeEqualityComparer.Instance);

        public NodeKind Kind { get; }

        public ClrType RootType { get; }

        public IReadOnlyCollection<ClrInstance> Instances => _instances;
        
        public TypeIndex(ClrType rootType, NodeKind kind)
        {
            RootType = rootType;
            Kind = kind;

            _derivedTypesAndRoot.Add(rootType);
        }

        public bool AddIfDerived(ClrType type)
        {
            if (_derivedTypesAndRoot.Contains(type.BaseType))
            {
                return _derivedTypesAndRoot.Add(type);
            }

            return false;
        }

        public void AddInstance(ClrInstance instance) => _instances.Add(instance);

        public IEnumerable<ClrType> GetTypesByFullName(string fullName)
        {
            return _derivedTypesAndRoot.Where(type => type.Name == fullName).ToList();
        }

        public bool ContainsType(ClrType type)
        {
            return type != null && _derivedTypesAndRoot.Contains(type);
        }
    }
}
