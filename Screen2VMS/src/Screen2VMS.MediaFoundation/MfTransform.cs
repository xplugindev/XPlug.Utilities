using System.Runtime.InteropServices;

namespace Screen2VMS.MediaFoundation;

/// <summary>
/// The Media Foundation Transform interface, used to drive the H.264 encoder.
/// </summary>
/// <remarks>
/// Declaration order is the vtable; see the note in <c>MfInterfaces.cs</c>.
/// Only the synchronous model is used. Hardware encoders generally present as
/// asynchronous MFTs, which need an event-driven pump and belong to the
/// hardware-optimisation milestone rather than the MVP (spec 71, v0.7).
/// </remarks>
[ComImport]
[Guid("bf94c121-5b05-4e6f-8000-ba598961414d")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFTransform
{
    [PreserveSig] int GetStreamLimits(
        out uint inputMinimum,
        out uint inputMaximum,
        out uint outputMinimum,
        out uint outputMaximum);

    [PreserveSig] int GetStreamCount(out uint inputStreams, out uint outputStreams);

    [PreserveSig] int GetStreamIDs(
        uint inputIdArraySize,
        [Out, MarshalAs(UnmanagedType.LPArray)] uint[] inputIds,
        uint outputIdArraySize,
        [Out, MarshalAs(UnmanagedType.LPArray)] uint[] outputIds);

    [PreserveSig] int GetInputStreamInfo(uint inputStreamId, out MftInputStreamInfo streamInfo);

    [PreserveSig] int GetOutputStreamInfo(uint outputStreamId, out MftOutputStreamInfo streamInfo);

    [PreserveSig] int GetAttributes(out IMFAttributes? attributes);

    [PreserveSig] int GetInputStreamAttributes(uint inputStreamId, out IMFAttributes? attributes);

    [PreserveSig] int GetOutputStreamAttributes(uint outputStreamId, out IMFAttributes? attributes);

    [PreserveSig] int DeleteInputStream(uint streamId);

    [PreserveSig] int AddInputStreams(uint streams, [In, MarshalAs(UnmanagedType.LPArray)] uint[] streamIds);

    [PreserveSig] int GetInputAvailableType(uint inputStreamId, uint typeIndex, out IMFMediaType? type);

    [PreserveSig] int GetOutputAvailableType(uint outputStreamId, uint typeIndex, out IMFMediaType? type);

    [PreserveSig] int SetInputType(uint inputStreamId, IMFMediaType? type, uint flags);

    [PreserveSig] int SetOutputType(uint outputStreamId, IMFMediaType? type, uint flags);

    [PreserveSig] int GetInputCurrentType(uint inputStreamId, out IMFMediaType? type);

    [PreserveSig] int GetOutputCurrentType(uint outputStreamId, out IMFMediaType? type);

    [PreserveSig] int GetInputStatus(uint inputStreamId, out uint flags);

    [PreserveSig] int GetOutputStatus(out uint flags);

    [PreserveSig] int SetOutputBounds(long lowerBound, long upperBound);

    [PreserveSig] int ProcessEvent(uint inputStreamId, IntPtr eventObject);

    [PreserveSig] int ProcessMessage(int message, IntPtr parameter);

    [PreserveSig] int ProcessInput(uint inputStreamId, IMFSample sample, uint flags);

    [PreserveSig] int ProcessOutput(
        uint flags,
        uint outputBufferCount,
        [In, Out, MarshalAs(UnmanagedType.LPArray)] MftOutputDataBuffer[] outputSamples,
        out uint status);
}

/// <summary>
/// Codec property access, used to force an IDR when a client joins.
/// </summary>
/// <remarks>
/// Only <c>SetValue</c> and <c>IsSupported</c> are typed; the rest hold their
/// vtable slots. VARIANT is marshalled as a raw pointer to a
/// <see cref="MfVariant"/> built by hand, which avoids dragging the full
/// OLE automation marshaller in for a single unsigned integer.
/// </remarks>
[ComImport]
[Guid("901db4c7-31ce-41a2-85dc-8fa0bf41b8da")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ICodecAPI
{
    [PreserveSig] int IsSupported(ref Guid api);

    [PreserveSig] int IsModifiable(ref Guid api);

    [PreserveSig] int GetParameterRange(ref Guid api, IntPtr valueMin, IntPtr valueMax, IntPtr steppingDelta);

    [PreserveSig] int GetParameterValues(ref Guid api, IntPtr values, out uint valuesCount);

    [PreserveSig] int GetDefaultValue(ref Guid api, IntPtr value);

    [PreserveSig] int GetValue(ref Guid api, IntPtr value);

    [PreserveSig] int SetValue(ref Guid api, IntPtr value);
}

[StructLayout(LayoutKind.Sequential)]
internal struct MftInputStreamInfo
{
    public long MaxLatency;
    public uint Flags;
    public uint Size;
    public uint MaxLookahead;
    public uint Alignment;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MftOutputStreamInfo
{
    public uint Flags;
    public uint Size;
    public uint Alignment;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MftOutputDataBuffer
{
    public uint StreamId;

    [MarshalAs(UnmanagedType.Interface)]
    public IMFSample? Sample;

    public uint Status;

    [MarshalAs(UnmanagedType.Interface)]
    public object? Events;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MftRegisterTypeInfo
{
    public Guid MajorType;
    public Guid Subtype;
}

/// <summary>
/// Just enough of a VARIANT to pass an unsigned integer to
/// <see cref="ICodecAPI.SetValue"/>.
/// </summary>
/// <remarks>
/// A real VARIANT is 24 bytes on x64: a 2-byte type tag, six bytes of padding
/// and reserved fields, then the 8-byte union.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct MfVariant
{
    public ushort Type;
    public ushort Reserved1;
    public ushort Reserved2;
    public ushort Reserved3;
    public ulong Value;

    /// <summary>VT_UI4.</summary>
    internal const ushort VtUi4 = 19;

    /// <summary>VT_BOOL.</summary>
    internal const ushort VtBool = 11;

    internal static MfVariant FromUInt32(uint value) => new() { Type = VtUi4, Value = value };

    /// <summary>VARIANT_TRUE is -1, not 1.</summary>
    internal static MfVariant FromBoolean(bool value) =>
        new() { Type = VtBool, Value = value ? unchecked((ulong)(short)-1) : 0 };
}
