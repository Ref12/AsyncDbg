using System;
using AsyncDbgCore.Core;
using Microsoft.Diagnostics.Runtime;

#nullable enable

namespace AsyncDbg.Core
{
    /// <summary>
    /// Represents a field of an object with its value.
    /// </summary>
    public sealed class ClrFieldValue
    {
        public ClrInstanceField Field { get; }
        public ClrInstance Instance { get; }

        private ClrFieldValue(ClrInstanceField field, ClrInstance instance)
        {
            Field = field ?? throw new ArgumentNullException(nameof(field));
            Instance = instance ?? throw new ArgumentNullException(nameof(instance));
        }

        internal static ClrFieldValue? Create(ClrInstanceField typeField, ClrInstance instance, bool interior)
        {
            if (typeField.Type.MetadataToken == 0 && typeField.Type.Name == "ERROR")
            {
                return null;
                // TODO: not sure about this!
                //throw new NotSupportedException();
            }

            Contract.AssertNotNull(instance.ObjectAddress);

            object? value = null;
            try
            {
                // This code could fail with NRE with no obvious reason.
                value = typeField.GetValue(instance.ObjectAddress.Value, interior);
            }
            catch(Exception)
            { }

            var type = typeField.Type;

            if (!type.IsIntrinsic() && value != null)
            {
                // instance.Heap.GetObjectType may return null. WHY?
                type = instance.Heap.GetObjectType((ulong)value) ?? type;
            }

            if (value == null)
            {
                value = instance.ObjectAddress.Value + (ulong)typeField.Offset;
            }

            return new ClrFieldValue(typeField, new ClrInstance(instance.Heap, value, type, interior));
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{Field.Name}: {Instance}";
        }
    }
}
