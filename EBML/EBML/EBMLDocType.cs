namespace EBML
{
   public struct EBMLDocType
   {
      public readonly string DocType;
      public readonly int Version;

      public EBMLDocType(string type, int version) { DocType = type; Version = version; }

      public bool CanReadDocument(EBMLHeader header)
      {
         if (header.DocType != DocType) { return false; }
         if (header.DocTypeReadVersion > Version) { return false; }
         return true;
      }
   }
}
