using System;
using AsyncDbgCore.Core;
using Microsoft.Diagnostics.Runtime;

namespace AsyncCausalityDebuggerNew
{
    /// <summary>
    /// Represents a field of an object with its value.
    /// </summary>
    public sealed class ClrFieldValue
    {
        public ClrInstanceField Field { get; } // NotNull
        public ClrInstance Instance { get; } // NotNull, Instance.IsNull may be true.

        private ClrFieldValue(ClrInstanceField field, ClrInstance instance)
        {
            Field = field ?? throw new ArgumentNullException(nameof(field));
            Instance = instance ?? throw new ArgumentNullException(nameof(instance));
        }

        internal static ClrFieldValue Create(ClrInstanceField typeField, ClrInstance instance, bool interior)
        {
            var getValueTypeField = typeField ?? throw new ArgumentNullException(nameof(typeField));
            instance = instance ?? throw new ArgumentNullException(nameof(instance));

            if (typeField.Type.MetadataToken == 0 && typeField.Type.Name == "ERROR")
            {
                return null;
                // TODO: not sure about this!
                //throw new NotSupportedException();
            }

            var value = getValueTypeField.GetValue(instance.ObjectAddress.Value, interior);

            var type = getValueTypeField.Type;

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
