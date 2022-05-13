using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EBML.Matroska
{
   public class MatroskaReader : IDisposable
   {
      private static readonly EBMLDocType[] docTypes = new EBMLDocType[]
      {
         new EBMLDocType("matroska", 4),
         new EBMLDocType("webm", 4),
      };

      private readonly EBMLDocumentReader document;
      private readonly MatroskaSeekHead seekHead = new();
      private readonly MatroskaInfo info = new();
      private readonly MatroskaTracks tracks = new();
      private readonly MatroskaCues cues = new();
      private int scanIndex;
      private bool readTrackInfo;

      public virtual EBMLDocType[] SupportedDocTypes => docTypes;
      public EBMLDocumentReader Document => document;
      public MatroskaSeekHead SeekHead => seekHead;
      public MatroskaInfo Info => info;
      public MatroskaTracks Tracks => tracks;
      public MatroskaCues Cues => cues;

      static MatroskaReader()
      {
         MatroskaSpecification.RegisterFormat();
      }

      public MatroskaReader(EBMLDocumentReader doc)
      {
         if (doc == null) { throw new ArgumentNullException(nameof(doc)); }
         var docTypes = SupportedDocTypes;
         var canRead = false;
         for (int i = docTypes.Length - 1; i >= 0; i--)
         {
            if (doc.CanBeReadBy(docTypes[i])) { canRead = true; break; }
         }
         if (!canRead)
         {
            throw new ArgumentException("Unsupported DocType: " + doc.Header.DocType, nameof(doc));
         }
         document = doc;
         if (doc.Body.Definition != MatroskaSpecification.Segment)
         {
            throw new ArgumentException("Expected Segment as document body", nameof(doc));
         }
      }

      public static async ValueTask<MatroskaReader> Read(IDataQueueReader reader, bool keepReaderOpen = false,
         DataBufferCache cache = null, CancellationToken cancellationToken = default)
      {
         var doc = await EBMLDocumentReader.Read(reader, keepReaderOpen, cache, cancellationToken);
         var matroska = new MatroskaReader(doc);
         await matroska.ReadTrackInfo(cancellationToken);
         return matroska;
      }

      public static async ValueTask<MatroskaReader> Read(Stream stream, bool keepStreamOpen = false,
         DataBufferCache cache = null, CancellationToken cancellationToken = default)
      {
         var doc = await EBMLDocumentReader.Read(stream, keepStreamOpen, cache, cancellationToken);
         var matroska = new MatroskaReader(doc);
         await matroska.ReadTrackInfo(cancellationToken);
         return matroska;
      }

      public static bool CanReadDocument(EBMLDocumentReader doc)
      {
         for (int i = docTypes.Length - 1; i >= 0; i--)
         {
            if (doc.CanBeReadBy(docTypes[i])) { return true; }
         }
         return false;
      }

      public void ScanTrackInfo()
      {
         var body = document.Body;
         for  (; scanIndex < body.Children.Length; scanIndex++)
         {
            var element = body.Children[scanIndex];
            if (element.Definition == MatroskaSpecification.SeekHead)
            {
               seekHead.AddSeekIndex(document.Reader, element as EBMLMasterElement, document.Body.DataOffset);
            }
            else if (element.Definition == MatroskaSpecification.Info)
            {
               info.ReadFrom(element as EBMLMasterElement);
            }
            else if (element.Definition == MatroskaSpecification.Tracks)
            {
               tracks.AddTrackEntry(element as EBMLMasterElement);
            }
            else if (element.Definition == MatroskaSpecification.Cues)
            {
               cues.AddCueEntry(element as EBMLMasterElement, document.Body.DataOffset);
            }
         }
      }

      public async ValueTask ReadTrackInfo(CancellationToken cancellationToken = default)
      {
         if (readTrackInfo) { return; }
         readTrackInfo = true;
         document.Reader.MaxInlineBinarySize = 4096;
         while (document.Reader.CurrentElement.Definition != MatroskaSpecification.Cluster)
         {
            if (await document.Reader.ReadNextElement(cancellationToken) == null) { break; }
         }
         ScanTrackInfo();
      }

      public virtual void Dispose()
      {
         document.Dispose();
      }
   }
}
