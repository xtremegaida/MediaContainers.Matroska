using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EBML
{
   public sealed class EBMLDocumentReader : IDisposable
   {
      private readonly EBMLReader reader;

      public EBMLHeader Header { get; private set; }
      public EBMLMasterElement Body { get; private set; }

      private EBMLDocumentReader(EBMLReader reader, EBMLHeader header, EBMLMasterElement body)
      {
         this.reader = reader;
         Header = header;
         Body = body;
      }

      public ValueTask<EBMLElement> ReadNextElement(CancellationToken cancellationToken = default)
      {
         return reader.ReadNextElement(cancellationToken);
      }

      private static async ValueTask<EBMLDocumentReader> Read(EBMLHeader header, EBMLReader reader, CancellationToken cancellationToken = default)
      {
         if (!EBMLDocTypeLookup.HandleEBMLDocType(header, reader))
         {
            throw new Exception("Unrecognised EBML doctype: " + header.DocType + " version " + header.DocTypeVersion);
         }
         var body = await reader.ReadNextElement(cancellationToken) as EBMLMasterElement;
         if (body == null) { throw new Exception("Expected a master element as document body"); }
         return new EBMLDocumentReader(reader, header, body);
      }

      public static async ValueTask<EBMLDocumentReader> Read(IDataQueueReader reader, DataBufferCache cache = null, CancellationToken cancellationToken = default)
      {
         var header = await EBMLHeader.Read(reader, cache, cancellationToken);
         var ebml = new EBMLReader(reader, true, cache);
         return await Read(header, ebml, cancellationToken);
      }

      public static async ValueTask<EBMLDocumentReader> Read(Stream stream, DataBufferCache cache = null, CancellationToken cancellationToken = default)
      {
         var header = await EBMLHeader.Read(stream, cache, cancellationToken);
         var ebml = new EBMLReader(stream, true, cache);
         return await Read(header, ebml, cancellationToken);
      }

      public void Dispose()
      {
         reader.Dispose();
      }
   }
}
