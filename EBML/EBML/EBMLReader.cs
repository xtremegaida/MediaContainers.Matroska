using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EBML
{
   public sealed class EBMLReader : IDisposable
   {
      private readonly Dictionary<ulong, EBMLElementDefiniton> elementTypes = new Dictionary<ulong, EBMLElementDefiniton>();
      private readonly DataBufferCache cache;
      private readonly IDataQueueReader reader;
      private readonly Stream stream;
      private readonly long startOffset;
      private readonly bool keepReaderOpen;
      private readonly byte[] readTmp = new byte[8];
      private readonly List<EBMLMasterElement> masterBlocks = new();
      private volatile bool disposed;
      private int maxInlineBinarySize = 4096;
      private EBMLMasterElement level;
      private EBMLElement lastElement;

      public EBMLMasterElement CurrentContainer => level;
      public EBMLElement CurrentElement => lastElement;
      public long CurrentReaderOffset => reader.TotalBytesRead + startOffset;
      public bool CanSeek => stream?.CanSeek ?? false;

      public int MaxInlineBinarySize
      {
         get { return maxInlineBinarySize; }
         set { maxInlineBinarySize = Math.Max(value, 0); }
      }

      public EBMLReader(IDataQueueReader reader, bool keepReaderOpen = false, DataBufferCache cache = null)
      {
         this.reader = reader;
         this.keepReaderOpen = keepReaderOpen;
         this.cache = cache ?? DataBufferCache.DefaultCache;
         EBMLElementDefiniton.AddGlobalElements(this);
      }

      public EBMLReader(Stream stream, bool keepStreamOpen = false, DataBufferCache cache = null)
         : this(new DataQueueStreamReader(stream, keepStreamOpen), false, cache)
      {
         this.stream = stream;
         if (stream.CanSeek) { startOffset = stream.Position; }
      }

      public void AddElementDefinition(EBMLElementDefiniton def)
      {
         elementTypes.Add(def.Id.ValueWithMarker, def);
      }

      public EBMLElementDefiniton GetElementDefinition(ulong id)
      {
         if (elementTypes.TryGetValue(id, out var def)) { return def; }
         return null;
      }

      private async ValueTask<EBMLSignedIntegerElement> ReadSignedInteger(EBMLElementDefiniton def, CancellationToken cancellationToken = default)
      {
         var reader = level?.Reader ?? this.reader;
         var size = await EBMLVInt.Read(reader, cancellationToken);
         if (size.IsEmpty) { return null; }
         if (size.IsUnknownValue || size.Value > 8) { return null; }
         var offset = CurrentReaderOffset;
         long value = 0;
         for (int i = (int)size.Value; i > 0; i--)
         {
            var read = await reader.ReadByteAsync(cancellationToken);
            if (read < 0) { return null; }
            value = (value << 8) | (byte)read;
         }
         var shift = 64 - ((int)size.Value << 3);
         value = (value << shift) >> shift;
         return new EBMLSignedIntegerElement(def, size, offset, value);
      }

      private async ValueTask<EBMLUnsignedIntegerElement> ReadUnsignedInteger(EBMLElementDefiniton def, CancellationToken cancellationToken = default)
      {
         var reader = level?.Reader ?? this.reader;
         var size = await EBMLVInt.Read(reader, cancellationToken);
         if (size.IsEmpty) { return null; }
         if (size.IsUnknownValue || size.Value > 8) { return null; }
         var offset = CurrentReaderOffset;
         ulong value = 0;
         for (int i = (int)size.Value; i > 0; i--)
         {
            var read = await reader.ReadByteAsync(cancellationToken);
            if (read < 0) { return null; }
            value = (value << 8) | (byte)read;
         }
         return new EBMLUnsignedIntegerElement(def, size, offset, value);
      }

      private async ValueTask<EBMLFloatElement> ReadFloat(EBMLElementDefiniton def, CancellationToken cancellationToken = default)
      {
         var reader = level?.Reader ?? this.reader;
         var size = await EBMLVInt.Read(reader, cancellationToken);
         if (size.IsEmpty) { return null; }
         if (size.IsUnknownValue) { return null; }
         var offset = CurrentReaderOffset;
         if (size.Value == 8)
         {
            await reader.ReadAsync(new Memory<byte>(readTmp, 0, 8), true, cancellationToken);
            var value = System.Buffers.Binary.BinaryPrimitives.ReadDoubleBigEndian(readTmp);
            return new EBMLFloatElement(def, size, offset, value);
         }
         if (size.Value == 4)
         {
            await reader.ReadAsync(new Memory<byte>(readTmp, 0, 4), true, cancellationToken);
            var value = System.Buffers.Binary.BinaryPrimitives.ReadSingleBigEndian(readTmp);
            return new EBMLFloatElement(def, size, offset, value);
         }
         return null;
      }
      
      private async ValueTask<EBMLStringElement> ReadString(EBMLElementDefiniton def, CancellationToken cancellationToken = default)
      {
         var reader = level?.Reader ?? this.reader;
         var size = await EBMLVInt.Read(reader, cancellationToken);
         if (size.IsEmpty) { return null; }
         if (size.IsUnknownValue) { return null; }
         var offset = CurrentReaderOffset;
         int readBytes = (int)size.Value;
         if (readBytes == 0) { return new EBMLStringElement(def, size, offset, string.Empty); }
         using (var buffer = cache.Pop(readBytes))
         {
            readBytes = await reader.ReadAsync(new Memory<byte>(buffer.Buffer, 0, readBytes), true, cancellationToken);
            var str = (def.Type == EBMLElementType.UTF8 ? Encoding.UTF8 : Encoding.ASCII).GetString(buffer.Buffer, 0, readBytes);
            return new EBMLStringElement(def, size, offset, str);
         }
      }

      private async ValueTask<EBMLDateElement> ReadDate(EBMLElementDefiniton def, CancellationToken cancellationToken = default)
      {
         var reader = level?.Reader ?? this.reader;
         var size = await EBMLVInt.Read(reader, cancellationToken);
         if (size.IsEmpty) { return null; }
         if (size.IsUnknownValue) { return null; }
         var offset = CurrentReaderOffset;
         if (size.Value == 0) { return new EBMLDateElement(def, size, offset, EBMLDateElement.Epoch); }
         if (size.Value == 8)
         {
            await reader.ReadAsync(new Memory<byte>(readTmp, 0, 8), true, cancellationToken);
            var value = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(readTmp);
            return new EBMLDateElement(def, size, offset, new DateTime(EBMLDateElement.Epoch.Ticks + (value / 100), DateTimeKind.Utc));
         }
         return null;
      }

      private async ValueTask<EBMLBinaryElement> ReadBinary(EBMLElementDefiniton def, CancellationToken cancellationToken = default)
      {
         var reader = level?.Reader ?? this.reader;
         var size = await EBMLVInt.Read(reader, cancellationToken);
         if (size.IsEmpty) { return null; }
         if (size.IsUnknownValue) { return null; }
         var offset = CurrentReaderOffset;
         if (size.Value == 0) { return new EBMLBinaryElement(def, size, offset, new ReadOnlyMemory<byte>()); }
         if (size.Value <= (ulong)maxInlineBinarySize)
         {
            var bytes = new byte[(int)size.Value];
            await reader.ReadAsync(bytes, true, cancellationToken);
            return new EBMLBinaryElement(def, size, offset, bytes);
         }
         return new EBMLBinaryElement(def, size, offset, reader);
      }

      private async ValueTask<EBMLVoidElement> ReadVoid(CancellationToken cancellationToken = default)
      {
         var reader = level?.Reader ?? this.reader;
         var size = await EBMLVInt.Read(reader, cancellationToken);
         if (size.IsEmpty) { return null; }
         if (size.IsUnknownValue) { return null; }
         var offset = CurrentReaderOffset;
         if (size.Value == 0) { return new EBMLVoidElement(size, offset); }
         if (size.Value <= (ulong)maxInlineBinarySize)
         {
            await reader.ReadAsync((int)size.Value, cancellationToken);
            return new EBMLVoidElement(size, offset);
         }
         return new EBMLVoidElement(size, offset, reader);
      }

      private async ValueTask<EBMLMasterElement> ReadMaster(EBMLElementDefiniton def, CancellationToken cancellationToken = default)
      {
         var reader = level?.Reader ?? this.reader;
         var size = await EBMLVInt.Read(reader, cancellationToken);
         if (size.IsEmpty) { return null; }
         return new EBMLMasterElement(def, size, CurrentReaderOffset, reader, this);
      }

      public async ValueTask<EBMLElement> ReadNextElement(CancellationToken cancellationToken = default)
      {
         if (lastElement != null && !lastElement.IsFullyRead && lastElement is IEBMLSkippableElement skip)
         {
            await skip.SkipToEnd(cancellationToken);
         }
         while (true)
         {
            var reader = level?.Reader ?? this.reader;
            var id = await EBMLVInt.Read(reader, cancellationToken);
            if (id.IsEmpty) { if (level == null) { return null; } PopLevel(); continue; }
            elementTypes.TryGetValue(id.ValueWithMarker, out var def);
            if (def != null)
            {
               while (level != null && level.DataSize.IsUnknownValue && !def.IsDirectChildOf(level.Definition)) { PopLevel(); }
               if (def == EBMLElementDefiniton.Void) { lastElement = await ReadVoid(cancellationToken); }
               else switch (def.Type)
               {
                  case EBMLElementType.SignedInteger: lastElement = await ReadSignedInteger(def, cancellationToken); break;
                  case EBMLElementType.UnsignedInteger: lastElement = await ReadUnsignedInteger(def, cancellationToken); break;
                  case EBMLElementType.Float: lastElement = await ReadFloat(def, cancellationToken); break;
                  case EBMLElementType.String: case EBMLElementType.UTF8: lastElement = await ReadString(def, cancellationToken); break;
                  case EBMLElementType.Date: lastElement = await ReadDate(def, cancellationToken); break;
                  case EBMLElementType.Master: lastElement = await ReadMaster(def, cancellationToken); break;
                  case EBMLElementType.Binary: default: lastElement = await ReadBinary(def, cancellationToken); break;
               }
            }
            else
            {
               lastElement = await ReadBinary(EBMLElementDefiniton.Unknown, cancellationToken);
            }
            level?.AddChild(lastElement);
            if (lastElement is EBMLMasterElement master)
            {
               if (level != null) { masterBlocks.Add(level); }
               level = master;
            }
            while (level != null && level.IsFullyRead) { PopLevel(); }
            return lastElement;
         }
      }

      private void PopLevel()
      {
         level.Reader.Dispose(); level = null;
         if (masterBlocks.Count > 0)
         {
            level = masterBlocks[masterBlocks.Count - 1];
            masterBlocks.RemoveAt(masterBlocks.Count - 1);
         }
      }

      public void Dispose()
      {
         if (disposed) { return; }
         disposed = true;
         while (level != null) { PopLevel(); }
         if (!keepReaderOpen) { reader.Dispose(); }
      }
   }
}
