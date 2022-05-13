using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EBML.Matroska
{
   public class MatroskaTracks : List<MatroskaTrackEntry>
   {
      public void AddTrackEntry(EBMLMasterElement element)
      {
         if (element == null) { return; }
         if (element.Definition == MatroskaSpecification.Tracks)
         {
            foreach (var child in element.Children)
            {
               AddTrackEntry(child as EBMLMasterElement);
            }
         }
         else if (element.Definition == MatroskaSpecification.TrackEntry)
         {
            var entry = new MatroskaTrackEntry();
            entry.ReadFrom(element);
            Add(entry);
         }
      }

      public async ValueTask Write(EBMLWriter writer, CancellationToken cancellationToken = default)
      {
         await writer.BeginMasterElement(MatroskaSpecification.Tracks, 4096, cancellationToken);
         for (int i = 0; i < Count; i++) { await this[i].Write(writer, cancellationToken); }
         await writer.EndMasterElement(cancellationToken);
      }

      public EBMLMasterElement ToElement()
      {
         var tracks = new EBMLMasterElement(MatroskaSpecification.Tracks);
         foreach (var track in this) { tracks.AddChild(track.ToElement()); }
         return tracks;
      }
   }
}
