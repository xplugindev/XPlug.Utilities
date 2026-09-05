using System.Runtime.InteropServices;
using System.Text;

namespace Screen2VMS.Camera.Interop;

/// <summary>
/// Media Foundation COM interfaces.
/// </summary>
/// <remarks>
/// <para>
/// Declaration order is the vtable, so nothing here may be reordered or
/// removed. Interfaces that derive from IMFAttributes repeat its thirty slots
/// first; slots Screen2VMS never calls are declared as Reserved placeholders
/// that hold their position without pulling in PROPVARIANT marshalling.
/// Calling one is a bug.
/// </para>
/// <para>
/// Every method is PreserveSig so HRESULTs are inspected rather than thrown: a
/// camera that is merely busy or unplugged has to be told apart from a real
/// fault (spec 31, 32).
/// </para>
/// </remarks>
[ComImport]
[Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFAttributes
{
    [PreserveSig] int Reserved01GetItem();

    [PreserveSig] int Reserved02GetItemType();

    [PreserveSig] int Reserved03CompareItem();

    [PreserveSig] int Reserved04Compare();

    [PreserveSig] int GetUINT32(ref Guid key, out uint value);

    [PreserveSig] int GetUINT64(ref Guid key, out ulong value);

    [PreserveSig] int Reserved07GetDouble();

    [PreserveSig] int GetGUID(ref Guid key, out Guid value);

    [PreserveSig] int GetStringLength(ref Guid key, out uint length);

    [PreserveSig] int GetString(ref Guid key, [Out] StringBuilder value, uint bufferSize, ref uint length);

    [PreserveSig] int GetAllocatedString(ref Guid key, out IntPtr value, out uint length);

    [PreserveSig] int Reserved12GetBlobSize();

    [PreserveSig] int Reserved13GetBlob();

    [PreserveSig] int Reserved14GetAllocatedBlob();

    [PreserveSig] int Reserved15GetUnknown();

    [PreserveSig] int Reserved16SetItem();

    [PreserveSig] int Reserved17DeleteItem();

    [PreserveSig] int Reserved18DeleteAllItems();

    [PreserveSig] int SetUINT32(ref Guid key, uint value);

    [PreserveSig] int SetUINT64(ref Guid key, ulong value);

    [PreserveSig] int Reserved21SetDouble();

    [PreserveSig] int SetGUID(ref Guid key, ref Guid value);

    [PreserveSig] int SetString(ref Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);

    [PreserveSig] int Reserved24SetBlob();

    [PreserveSig] int Reserved25SetUnknown();

    [PreserveSig] int Reserved26LockStore();

    [PreserveSig] int Reserved27UnlockStore();

    [PreserveSig] int Reserved28GetCount();

    [PreserveSig] int Reserved29GetItemByIndex();

    [PreserveSig] int Reserved30CopyAllItems();
}

[ComImport]
[Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFMediaType
{
    // IMFAttributes, slots 1-30.
    [PreserveSig] int Reserved01GetItem();

    [PreserveSig] int Reserved02GetItemType();

    [PreserveSig] int Reserved03CompareItem();

    [PreserveSig] int Reserved04Compare();

    [PreserveSig] int GetUINT32(ref Guid key, out uint value);

    [PreserveSig] int GetUINT64(ref Guid key, out ulong value);

    [PreserveSig] int Reserved07GetDouble();

    [PreserveSig] int GetGUID(ref Guid key, out Guid value);

    [PreserveSig] int Reserved09GetStringLength();

    [PreserveSig] int Reserved10GetString();

    [PreserveSig] int Reserved11GetAllocatedString();

    [PreserveSig] int Reserved12GetBlobSize();

    [PreserveSig] int Reserved13GetBlob();

    [PreserveSig] int Reserved14GetAllocatedBlob();

    [PreserveSig] int Reserved15GetUnknown();

    [PreserveSig] int Reserved16SetItem();

    [PreserveSig] int Reserved17DeleteItem();

    [PreserveSig] int Reserved18DeleteAllItems();

    [PreserveSig] int SetUINT32(ref Guid key, uint value);

    [PreserveSig] int SetUINT64(ref Guid key, ulong value);

    [PreserveSig] int Reserved21SetDouble();

    [PreserveSig] int SetGUID(ref Guid key, ref Guid value);

    [PreserveSig] int Reserved23SetString();

    [PreserveSig] int Reserved24SetBlob();

    [PreserveSig] int Reserved25SetUnknown();

    [PreserveSig] int Reserved26LockStore();

    [PreserveSig] int Reserved27UnlockStore();

    [PreserveSig] int Reserved28GetCount();

    [PreserveSig] int Reserved29GetItemByIndex();

    [PreserveSig] int Reserved30CopyAllItems();

    // IMFMediaType, slots 31-35.
    [PreserveSig] int GetMajorType(out Guid majorType);

    [PreserveSig] int Reserved32IsCompressedFormat();

    [PreserveSig] int Reserved33IsEqual();

    [PreserveSig] int Reserved34GetRepresentation();

    [PreserveSig] int Reserved35FreeRepresentation();
}

[ComImport]
[Guid("7fee9e9a-4a89-47a6-899c-b6a53a70fb67")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFActivate
{
    // IMFAttributes, slots 1-30.
    [PreserveSig] int Reserved01GetItem();

    [PreserveSig] int Reserved02GetItemType();

    [PreserveSig] int Reserved03CompareItem();

    [PreserveSig] int Reserved04Compare();

    [PreserveSig] int Reserved05GetUINT32();

    [PreserveSig] int Reserved06GetUINT64();

    [PreserveSig] int Reserved07GetDouble();

    [PreserveSig] int Reserved08GetGUID();

    [PreserveSig] int GetStringLength(ref Guid key, out uint length);

    [PreserveSig] int GetString(ref Guid key, [Out] StringBuilder value, uint bufferSize, ref uint length);

    [PreserveSig] int GetAllocatedString(ref Guid key, out IntPtr value, out uint length);

    [PreserveSig] int Reserved12GetBlobSize();

    [PreserveSig] int Reserved13GetBlob();

    [PreserveSig] int Reserved14GetAllocatedBlob();

    [PreserveSig] int Reserved15GetUnknown();

    [PreserveSig] int Reserved16SetItem();

    [PreserveSig] int Reserved17DeleteItem();

    [PreserveSig] int Reserved18DeleteAllItems();

    [PreserveSig] int Reserved19SetUINT32();

    [PreserveSig] int Reserved20SetUINT64();

    [PreserveSig] int Reserved21SetDouble();

    [PreserveSig] int Reserved22SetGUID();

    [PreserveSig] int Reserved23SetString();

    [PreserveSig] int Reserved24SetBlob();

    [PreserveSig] int Reserved25SetUnknown();

    [PreserveSig] int Reserved26LockStore();

    [PreserveSig] int Reserved27UnlockStore();

    [PreserveSig] int Reserved28GetCount();

    [PreserveSig] int Reserved29GetItemByIndex();

    [PreserveSig] int Reserved30CopyAllItems();

    // IMFActivate, slots 31-33.
    [PreserveSig] int ActivateObject(ref Guid riid, out IntPtr instance);

    [PreserveSig] int ShutdownObject();

    [PreserveSig] int DetachObject();
}

[ComImport]
[Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSourceReader
{
    [PreserveSig] int GetStreamSelection(uint streamIndex, [MarshalAs(UnmanagedType.Bool)] out bool selected);

    [PreserveSig] int SetStreamSelection(uint streamIndex, [MarshalAs(UnmanagedType.Bool)] bool selected);

    [PreserveSig] int GetNativeMediaType(uint streamIndex, uint mediaTypeIndex, out IMFMediaType? mediaType);

    [PreserveSig] int GetCurrentMediaType(uint streamIndex, out IMFMediaType? mediaType);

    [PreserveSig] int SetCurrentMediaType(uint streamIndex, IntPtr reserved, IMFMediaType mediaType);

    [PreserveSig] int SetCurrentPosition(ref Guid timeFormat, IntPtr position);

    [PreserveSig] int ReadSample(
        uint streamIndex,
        uint controlFlags,
        out uint actualStreamIndex,
        out uint streamFlags,
        out long timestamp,
        out IMFSample? sample);

    [PreserveSig] int Flush(uint streamIndex);

    [PreserveSig] int GetServiceForStream(uint streamIndex, ref Guid service, ref Guid riid, out IntPtr instance);

    [PreserveSig] int GetPresentationAttribute(uint streamIndex, ref Guid attribute, IntPtr value);
}

[ComImport]
[Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFSample
{
    // IMFAttributes, slots 1-30.
    [PreserveSig] int Reserved01GetItem();

    [PreserveSig] int Reserved02GetItemType();

    [PreserveSig] int Reserved03CompareItem();

    [PreserveSig] int Reserved04Compare();

    [PreserveSig] int Reserved05GetUINT32();

    [PreserveSig] int Reserved06GetUINT64();

    [PreserveSig] int Reserved07GetDouble();

    [PreserveSig] int Reserved08GetGUID();

    [PreserveSig] int Reserved09GetStringLength();

    [PreserveSig] int Reserved10GetString();

    [PreserveSig] int Reserved11GetAllocatedString();

    [PreserveSig] int Reserved12GetBlobSize();

    [PreserveSig] int Reserved13GetBlob();

    [PreserveSig] int Reserved14GetAllocatedBlob();

    [PreserveSig] int Reserved15GetUnknown();

    [PreserveSig] int Reserved16SetItem();

    [PreserveSig] int Reserved17DeleteItem();

    [PreserveSig] int Reserved18DeleteAllItems();

    [PreserveSig] int Reserved19SetUINT32();

    [PreserveSig] int Reserved20SetUINT64();

    [PreserveSig] int Reserved21SetDouble();

    [PreserveSig] int Reserved22SetGUID();

    [PreserveSig] int Reserved23SetString();

    [PreserveSig] int Reserved24SetBlob();

    [PreserveSig] int Reserved25SetUnknown();

    [PreserveSig] int Reserved26LockStore();

    [PreserveSig] int Reserved27UnlockStore();

    [PreserveSig] int Reserved28GetCount();

    [PreserveSig] int Reserved29GetItemByIndex();

    [PreserveSig] int Reserved30CopyAllItems();

    // IMFSample, slots 31-44.
    [PreserveSig] int GetSampleFlags(out uint flags);

    [PreserveSig] int SetSampleFlags(uint flags);

    [PreserveSig] int GetSampleTime(out long time);

    [PreserveSig] int SetSampleTime(long time);

    [PreserveSig] int GetSampleDuration(out long duration);

    [PreserveSig] int SetSampleDuration(long duration);

    [PreserveSig] int GetBufferCount(out uint count);

    [PreserveSig] int GetBufferByIndex(uint index, out IMFMediaBuffer? buffer);

    [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer? buffer);

    [PreserveSig] int Reserved40AddBuffer();

    [PreserveSig] int Reserved41RemoveBufferByIndex();

    [PreserveSig] int Reserved42RemoveAllBuffers();

    [PreserveSig] int GetTotalLength(out uint length);

    [PreserveSig] int Reserved44CopyToBuffer();
}

[ComImport]
[Guid("045fa593-8799-42b8-bc8d-8968c6453507")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFMediaBuffer
{
    [PreserveSig] int Lock(out IntPtr buffer, out int maxLength, out int currentLength);

    [PreserveSig] int Unlock();

    [PreserveSig] int GetCurrentLength(out int length);

    [PreserveSig] int SetCurrentLength(int length);

    [PreserveSig] int GetMaxLength(out int length);
}

[ComImport]
[Guid("7dc9d5f9-9ed9-44ec-9bbf-0600bb589fbb")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMF2DBuffer
{
    [PreserveSig] int Lock2D(out IntPtr scanline0, out int pitch);

    [PreserveSig] int Unlock2D();

    [PreserveSig] int GetScanline0AndPitch(out IntPtr scanline0, out int pitch);

    [PreserveSig] int IsContiguousFormat([MarshalAs(UnmanagedType.Bool)] out bool contiguous);

    [PreserveSig] int GetContiguousLength(out int length);

    [PreserveSig] int Reserved06ContiguousCopyTo();

    [PreserveSig] int Reserved07ContiguousCopyFrom();
}

[ComImport]
[Guid("279a808d-aec7-40c8-9c6b-a6b492c78a66")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMFMediaSource
{
    // IMFMediaEventGenerator, slots 1-4.
    [PreserveSig] int Reserved01GetEvent();

    [PreserveSig] int Reserved02BeginGetEvent();

    [PreserveSig] int Reserved03EndGetEvent();

    [PreserveSig] int Reserved04QueueEvent();

    // IMFMediaSource, slots 5-10.
    [PreserveSig] int GetCharacteristics(out uint characteristics);

    [PreserveSig] int Reserved06CreatePresentationDescriptor();

    [PreserveSig] int Reserved07Start();

    [PreserveSig] int Stop();

    [PreserveSig] int Pause();

    [PreserveSig] int Shutdown();
}
