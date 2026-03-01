using System.Collections.Immutable;
using System.Reflection.Metadata;


namespace UnifierTSL.Reflection.Metadata.DecodeProviders
{
    // Only decodes primitive types
    public class SimpleTypeProvider : ISignatureTypeProvider<Type, object?>
    {
        public Type GetPrimitiveType(PrimitiveTypeCode typeCode) =>
            typeCode switch {
                PrimitiveTypeCode.Void => typeof(void),
                PrimitiveTypeCode.Boolean => typeof(bool),
                PrimitiveTypeCode.SByte => typeof(sbyte),
                PrimitiveTypeCode.Byte => typeof(byte),
                PrimitiveTypeCode.Int16 => typeof(short),
                PrimitiveTypeCode.UInt16 => typeof(ushort),
                PrimitiveTypeCode.Int32 => typeof(int),
                PrimitiveTypeCode.UInt32 => typeof(uint),
                PrimitiveTypeCode.Int64 => typeof(long),
                PrimitiveTypeCode.UInt64 => typeof(ulong),
                PrimitiveTypeCode.Single => typeof(float),
                PrimitiveTypeCode.Double => typeof(double),
                PrimitiveTypeCode.IntPtr => typeof(nuint),
                PrimitiveTypeCode.UIntPtr => typeof(nuint),
                PrimitiveTypeCode.Char => typeof(char),
                PrimitiveTypeCode.String => typeof(string),
                PrimitiveTypeCode.Object => typeof(object),
                PrimitiveTypeCode.TypedReference => typeof(TypedReference),
                _ => throw new NotSupportedException($"Primitive type {typeCode} not supported")
            };
        public Type GetArrayType(Type elementType, ArrayShape shape) => typeof(Array); // Not supported here
        public Type GetByReferenceType(Type elementType) => elementType;
        public Type GetFunctionPointerType(MethodSignature<Type> signature) => typeof(nint);
        public Type GetGenericInstantiation(Type genericType, ImmutableArray<Type> typeArguments) => typeof(object);
        public Type GetGenericMethodParameter(object? genericContext, int index) => typeof(object);
        public Type GetGenericTypeParameter(object? genericContext, int index) => typeof(object);
        public Type GetModifiedType(Type modifier, Type unmodifiedType, bool isRequired) => unmodifiedType;
        public Type GetPinnedType(Type elementType) => elementType;
        public Type GetPointerType(Type elementType) => typeof(nint);
        public Type GetSZArrayType(Type elementType) => elementType.MakeArrayType();
        public Type GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => typeof(object);

        public Type GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => typeof(object);
        public Type GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => typeof(object);
    }
}
