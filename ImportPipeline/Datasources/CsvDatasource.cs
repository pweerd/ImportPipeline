/*
 * Licensed to De Bitmanager under one or more contributor
 * license agreements. See the NOTICE file distributed with
 * this work for additional information regarding copyright
 * ownership. De Bitmanager licenses this file to you under
 * the Apache License, Version 2.0 (the "License"); you may
 * not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *    http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing,
 * software distributed under the License is distributed on an
 * "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
 * KIND, either express or implied.  See the License for the
 * specific language governing permissions and limitations
 * under the License.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bitmanager.Core;
using Bitmanager.Xml;
using System.Xml;
using Newtonsoft.Json.Linq;
using Bitmanager.Json;
using System.IO;
using System.Globalization;
using Bitmanager.Elastic;
using Bitmanager.IO;

namespace Bitmanager.ImportPipeline
{
   public class CsvDatasource: Datasource
   {
      enum _HeaderOptions { False, True, UseForFieldNames };
      private IDatasourceFeeder feeder;
      List<String> initialFieldNames;
      //DSString file;
      //int[] sortValuesToKeep;
      int sortKey;
      int startAt;

      String escChar;
      char delimChar, quoteChar, commentChar;
      _HeaderOptions headerOptions;
      CsvLenientMode lenient;
      CsvTrimOptions trim;

      public void Init(PipelineContext ctx, XmlNode node)
      {
         feeder = ctx.CreateFeeder(node, typeof (FileNameFeeder));
         //DS file = ctx.ImportEngine.Xml.CombinePath(node.ReadStr("@file"));
         headerOptions = node.ReadEnum("@headers", _HeaderOptions.False);
         String fieldNames = node.ReadStr("@fieldnames", null);
         initialFieldNames = createInitialFieldNames(fieldNames);
         if (initialFieldNames != null && headerOptions == _HeaderOptions.UseForFieldNames)
            throw new BMNodeException(node, "Cannot specify both  fieldnames and headers=UseForFieldNames.");

         lenient = node.ReadEnum("@lenient", CsvLenientMode.False);
         trim = node.ReadEnum("@trim", CsvTrimOptions.None);
         escChar = node.ReadStr("@escape", null);

         delimChar = readChar(node, "@dlm", ',');
         quoteChar = readChar(node, "@quote", '"');
         commentChar = readChar(node, "@comment", '#');
         startAt = node.ReadInt("@startat", -1);

         String sort = node.ReadStr("@sort", null);
         sortKey = -1;
         if (sort != null)
         {
            sortKey = interpretField(sort);
         }
      }

      private static int interpretField(String x)
      {
         switch (x[0])
         {
            case 'f':
            case 'F': x = x.Substring(1); break;
         }
         return Invariant.ToInt32(x);
      }

      internal static char readChar(XmlNode node, String attr, char def)
      {
         String v = node.ReadStr (attr, null);
         if (v==null) return def;

         int x;
         switch (v.Length)
         {
            case 1: return v[0];
            case 4:
               if (v.StartsWith(@"0x", StringComparison.InvariantCultureIgnoreCase)) goto TRY_CONVERT;
               goto ERROR;
            case 6:
               if (v.StartsWith(@"0x", StringComparison.InvariantCultureIgnoreCase)) goto TRY_CONVERT;
               if (v.StartsWith(@"\u", StringComparison.InvariantCultureIgnoreCase)) goto TRY_CONVERT;
               goto ERROR;
         }
         goto ERROR;

         TRY_CONVERT:
         if (int.TryParse(v.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out x))
            return (char)x;

      ERROR:
         throw new BMNodeException (node, "Invalid character({0}) at expression {1}. Must be: single char, \\uXXXX, 0xXX", v, attr);
      }

      public void Import(PipelineContext ctx, IDatasourceSink sink)
      {
         ctx.Emitted = 0;
         foreach (var elt in feeder.GetElements(ctx))
         {
            try
            {
               String file = elt.Element.ToString();
               ctx.ImportLog.Log("-- Importing {0}...", file);
               if (sortKey < 0) processFile(ctx, file, sink);
               else processSortedFile(ctx, file, sink);
            }
            catch (Exception e)
            {
               throw new BMException(e, e.Message + "\r\nFile=" + elt.Element + ".");
            }
         }
      }

      protected void processFile(PipelineContext ctx, String fileName, IDatasourceSink sink)
      {
         List<String> keys;
         ctx.SendItemStart(fileName);

         using (FileStream strm = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
         {
            CsvReader csvRdr = createReader(strm);
            optReadHeader(csvRdr);
            keys = createKeysForEmit();
            int startAt = this.startAt;
            while (csvRdr.NextRecord())
            {
               if (startAt>0 && startAt > csvRdr.Line) continue;
               ctx.IncrementEmitted();
               sink.HandleValue(ctx, "record/_start", null);
               var fields = csvRdr.Fields;
               int fieldCount = fields.Count;
               //ctx.DebugLog.Log("Record {0}. FC={1}", line, fieldCount); 
               generateMissingKeysForEmit(keys, fieldCount);
               for (int i = 0; i < fieldCount; i++)
               {
                  sink.HandleValue(ctx, keys[i], fields[i]);
               }
               sink.HandleValue(ctx, "record", null);
            }
            if (csvRdr.NumInvalidRecords>0)
               ctx.ImportLog.Log(_LogType.ltWarning, "Invalid records detected: {0}", csvRdr.NumInvalidRecords);
         }
         ctx.SendItemStop();
      }

      private void optReadHeader(CsvReader rdr)
      {
         if (headerOptions == _HeaderOptions.False) return;
         if (!rdr.NextRecord()) return;

         switch (headerOptions)
         {
            case _HeaderOptions.True: return;
            case _HeaderOptions.UseForFieldNames:
               initialFieldNames = replaceEmptyNames(rdr.Fields.ToList());
               break;
         }
      }


      private int cbSortString(String[] a, String[] b)
      {
         return StringComparer.OrdinalIgnoreCase.Compare(a[0], b[0]);
      }
      CsvReader createReader(FileStream strm)
      {
         CsvReader rdr = new CsvReader(strm);
         rdr.QuoteOrd = (int)quoteChar;
         rdr.SepOrd = (int)delimChar;
         if (escChar != null) rdr.EscapeChar = escChar;
         //rdr.SkipHeader = hasHeaders;
         rdr.LenientMode = lenient;
         rdr.SkipEmptyRecords = true;
         rdr.TrimOptions = trim;
         //Logs.ErrorLog.Log("Multiline={0}, quote={1} ({2}), esc={3} ({4}), startat={5}", true, csvRdr.QuoteChar, (int)csvRdr.QuoteOrd, csvRdr.EscapeOrd, (int)csvRdr.SepOrd, startAt);
         return rdr;
      }
      protected void processSortedFile(PipelineContext ctx, String fileName, IDatasourceSink sink)
      {
         List<String[]> rows = new List<string[]>();

         int maxFieldCount = 0;
         using (FileStream strm = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
         {
            CsvReader csvRdr = createReader(strm);
            optReadHeader(csvRdr);
            int startAt = this.startAt;
            while (csvRdr.NextRecord())
            {
               if (startAt>0 && startAt > csvRdr.Line) continue;
               var fields = csvRdr.Fields;
               int fieldCount = fields.Count;
               if (fieldCount > maxFieldCount) maxFieldCount = fieldCount;
               String[] arr = new String[fieldCount+1];

               for (int i = 0; i < fieldCount; i++) arr[i+1] = fields[i];
               if (fieldCount > sortKey) arr[0] = arr[sortKey+1];
               rows.Add (arr);
            }
         }

         ctx.DebugLog.Log("First 10 sortkeys:");
         int N = rows.Count;
         if (N > 10) N = 10;
         for (int i = 0; i < N; i++)
         {
            ctx.DebugLog.Log("-- [{0}]: '{1}'", i, rows[i][0]);
         }

         rows.Sort (cbSortString);

         ctx.DebugLog.Log("First 10 sortkeys after sort:");
         for (int i = 0; i < N; i++)
         {
            ctx.DebugLog.Log("-- [{0}]: '{1}'", i, rows[i][0]);
         }

         //Fill pre-calculated keys
         List<String> keys = createKeysForEmit();
         generateMissingKeysForEmit(keys, maxFieldCount);

         //Emit sorted records
         ctx.SendItemStart (fileName);
         for (int r = 0; r < rows.Count; r++)
         {
            ctx.IncrementEmitted();
            String[] arr = rows[r];
            rows[r] = null; //Let this element be GC-ed
            sink.HandleValue(ctx, "record/_start", null);
            for (int i = 1; i < arr.Length; i++) //arr[0] is the sortkey
            {
               sink.HandleValue(ctx, keys[i-1], arr[i]);
            }
            sink.HandleValue(ctx, "record", null);
         }
         ctx.SendItemStop(fileName);
      }

      private static List<String> createInitialFieldNames(String fields)
      {
         if (String.IsNullOrEmpty(fields)) return null;
         using (var strm = IOUtils.CreateStreamFromString(fields))
         {
            int dlm, quote;
            CsvReader.GetBestSeparators(null, strm.CreateTextReader(), out dlm, out quote);
            strm.Position = 0;
            var rdr = new CsvReader(strm, null);
            rdr.QuoteOrd = quote;
            rdr.SepOrd = dlm;
            if (rdr.NextRecord())
            {
               return replaceEmptyNames(rdr.Fields.ToList());
            }
         }
         return null;
      }

      private static List<String> replaceEmptyNames(List<String> list)
      {
         if (list == null) return list;
         for (int i = 0; i < list.Count; i++)
         {
            String x = list[i].TrimToNull();
            list[i] = x == null ? String.Format("f{0}", i) : x;
         }
         return list;
      }

      private List<String> createKeysForEmit()
      {
         return initialFieldNames == null ? new List<string>() : initialFieldNames.Select(s => "record/" + s).ToList();
      }

      private static void generateMissingKeysForEmit(List<String> list, int needed)
      {
         for (int i = list.Count; i <= needed; i++) list.Add(String.Format("record/f{0}", i));
      }
   }

}
