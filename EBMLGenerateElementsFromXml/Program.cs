using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace MediaContainersGenerateElementsFromXml
{
   class Program
   {
      static int Main(string[] args)
      {
         args = new string[] { "..\\..\\..\\ebml_matroska.xml" };
         if (args.Length == 0)
         {
            Console.WriteLine("EBMLGenerateElementsFromXml <input.xml> [output.cs]");
            return -1;
         }
         
         var inputFile = args[0];
         var outputFile = args.Length >= 2 ? args[1] : (Path.Combine(Path.GetDirectoryName(inputFile), Path.GetFileNameWithoutExtension(inputFile) + ".cs"));
         var xml = new XmlDocument();
         xml.LoadXml(File.ReadAllText(inputFile));
         var elements = new List<EBMLElementDefiniton>();

         foreach (XmlNode node in xml.FirstChild.ChildNodes)
         {
            if (node.NodeType != XmlNodeType.Element || node.Name.ToLower() != "element") { continue; }
            var element = new EBMLElementDefiniton() { Type = "EBMLElementType.Unknown" };
            foreach (XmlNode attr in node.Attributes)
            {
               if (attr.Name == "id") { element.Id = attr.Value; }
               else if (attr.Name == "name") { element.CodeName = attr.Value; }
               else if (attr.Name == "path") { element.FullPath = attr.Value; }
               else if (attr.Name == "unknownsizeallowed" && attr.Value == "1") { element.AllowUnknownSize = true; }
               else if (attr.Name == "default") { element.DefaultValue = attr.Value; }
               else if (attr.Name == "type")
               {
                  switch (attr.Value.ToLower())
                  {
                     case "uinteger": element.Type = "EBMLElementType.UnsignedInteger"; break;
                     case "integer": element.Type = "EBMLElementType.SignedInteger"; break;
                     case "float": element.Type = "EBMLElementType.Float"; break;
                     case "string": element.Type = "EBMLElementType.String"; break;
                     case "utf-8": element.Type = "EBMLElementType.UTF8"; break;
                     case "date": element.Type = "EBMLElementType.Date"; break;
                     case "master": element.Type = "EBMLElementType.Master"; break;
                     case "binary": element.Type = "EBMLElementType.Binary"; break;
                  }
               }
            }
            if (string.IsNullOrWhiteSpace(element.Id)) { continue; }
            elements.Add(element);
         }

         var result = new StringBuilder();
         result.AppendLine("namespace MediaContainers");
         result.AppendLine("{");
         result.AppendLine("   public static class ElementDefinition");
         result.AppendLine("   {");
         for (int i = 0; i < elements.Count; i++)
         {
            result.Append("      public static readonly EBMLElementDefiniton ");
            result.Append(elements[i].CodeName);
            result.Append(" = new EBMLElementDefiniton(");
            result.Append(elements[i].Id);
            result.Append(", ");
            result.Append(elements[i].Type);
            result.Append(", @\"");
            result.Append(elements[i].FullPath);
            result.Append('\"');
            if (elements[i].AllowUnknownSize) { result.Append(", allowUnknownSize: true"); }
            if (!string.IsNullOrWhiteSpace(elements[i].DefaultValue))
            {
               result.Append(", defaultVal: \"");
               result.Append(elements[i].DefaultValue);
               result.Append('"');
            }
            result.AppendLine(");");
         }
         result.AppendLine();
         result.AppendLine("      private static readonly EBMLElementDefiniton[] elements = new EBMLElementDefiniton[]");
         result.AppendLine("      {");
         for (int i = 0; i < elements.Count;)
         {
            var line = elements.Skip(i).Take(10).ToArray();
            result.Append("         ");
            result.Append(string.Join(", ", line.Select(x => x.CodeName)));
            result.AppendLine(",");
            i += line.Length;
         }
         result.AppendLine("      };");
         result.AppendLine();
         result.AppendLine("      public static void AddElements(EBMLReader reader)");
         result.AppendLine("      {");
         result.AppendLine("         for (int i = 0; i < elements.Length; i++)");
         result.AppendLine("         {");
         result.AppendLine("            reader.AddElementDefinition(elements[i]);");
         result.AppendLine("         }");
         result.AppendLine("      }");
         result.AppendLine("   }");
         result.AppendLine("}");
         File.WriteAllText(outputFile, result.ToString());
         return 0;
      }

      private class EBMLElementDefiniton
      {
         public string CodeName;
         public string Id;
         public string Type;
         public string FullPath;
         public bool AllowUnknownSize;
         public string DefaultValue;
      }
   }
}
