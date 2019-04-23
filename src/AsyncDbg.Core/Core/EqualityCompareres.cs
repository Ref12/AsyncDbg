using System.Collections.Generic;
using AsyncDbg.Core;
using Microsoft.Diagnostics.Runtime;

#nullable enable

namespace AsyncDbg.Core
{
    internal class ClrInstanceAddressComparer : EqualityComparer<ClrInstance>
    {
        public static readonly ClrInstanceAddressComparer Instance = new ClrInstanceAddressComparer();

        public override bool Equals(ClrInstance x, ClrInstance y)
        {
            if (x?.ObjectAddress == null || y?.ObjectAddress == null)
            {
                return false;
            }

            return x?.ObjectAddress == y?.ObjectAddress;
        }

        public override int GetHashCode(ClrInstance obj)
        {
            return obj?.ObjectAddress?.GetHashCode() ?? 0;
        }
    }

    internal class ClrTypeEqualityComparer : EqualityComparer<ClrType?>
    {
        public static readonly ClrTypeEqualityComparer Instance = new ClrTypeEqualityComparer();
        public override bool Equals(ClrType? x, ClrType? y)
        {
            return (x?.MetadataToken == y?.MetadataToken || x?.Name == y?.Name) && x?.Module.FileName == y?.Module.FileName;
        }

        public override int GetHashCode(ClrType? obj)
        {
            return (obj?.MetadataToken.GetHashCode() ?? 0) ^ (obj?.Module.FileName?.GetHashCode() ?? 1);
        }
    }
}
