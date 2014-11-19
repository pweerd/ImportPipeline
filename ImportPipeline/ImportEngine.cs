﻿using Bitmanager.Core;
using Bitmanager.Elastic;
using Bitmanager.IO;
using Bitmanager.Xml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;

namespace Bitmanager.ImportPipeline
{
   [Flags]
   public enum _ImportFlags
   {
      ImportFull = 1 << 0,
      FullImport = 1 << 0,
      DoNotRename = 1 << 1,
      TraceValues = 1<<2,
      IgnoreErrors = 1<<3,
      IgnoreLimited = 1<<4,
      IgnoreAll = IgnoreErrors | IgnoreLimited,
      UseFlagsFromXml = 1 << 5,
      Silent = 1 << 6,
      RetryErrors = 1 << 7,
   }
   public class ImportEngine
   {
      public XmlHelper Xml { get; private set; }
      public Endpoints Endpoints;
      public Converters Converters;
      public NamedAdminCollection<DatasourceAdmin> Datasources;
      public NamedAdminCollection<Pipeline> Pipelines;
      public ProcessHostCollection JavaHostCollection;
      public ScriptHost ScriptHost;
      public readonly Logger ImportLog;
      public readonly Logger DebugLog;
      public readonly Logger ErrorLog;
      public readonly Logger MissedLog;
      public DateTime StartTimeUtc { get; private set; }
      public int LogAdds { get; set; }
      public int MaxAdds { get; set; }
      public _ImportFlags ImportFlags { get; set; }


      public ImportEngine()
      {
         ImportLog = Logs.CreateLogger("import", "ImportEngine");
         DebugLog = Logs.CreateLogger("import-debug", "ImportEngine");
         MissedLog = Logs.CreateLogger("import-missed", "ImportEngine");
         ErrorLog = Logs.ErrorLog.Clone("ImportEngine");
         Logs.DebugLog.Log(((InternalLogger)ImportLog)._Logger.Name);
         LogAdds = 50000;
         MaxAdds = -1;
      }
      public void Load(String fileName)
      {
         XmlHelper xml = new XmlHelper(fileName);
         Load(xml);
      }

      public void Load(XmlHelper xml)
      {
         Xml = xml;
         String dir = xml.FileName;
         if (!String.IsNullOrEmpty(dir)) dir = Path.GetDirectoryName(xml.FileName);
         Environment.SetEnvironmentVariable("IMPORT_DIR", dir);
         fillTikaVars();

         PipelineContext ctx = new PipelineContext(this);
         ImportFlags = xml.ReadEnum("@importflags", ImportFlags);
         LogAdds = xml.ReadInt("@logadds", LogAdds);
         MaxAdds = xml.ReadInt("@maxadds", MaxAdds);
         ImportLog.Log("Loading import xml: flags={0}, logadds={1}, maxadds={2}", ImportFlags, LogAdds, MaxAdds);

         //Load the supplied script
         ImportLog.Log(_LogType.ltTimerStart, "loading: scripts"); 
         XmlNode scriptNode = xml.SelectSingleNode("script");
         if (scriptNode != null)
         {
            ScriptHost = new ScriptHost();
            String fn = xml.CombinePath (scriptNode.ReadStr("@file"));
            ScriptHost.AddFile(fn);
            ScriptHost.AddReference(Assembly.GetExecutingAssembly());
            ScriptHost.Compile();
         }

         ImportLog.Log(_LogType.ltTimer, "loading: helper process definitions ");
         JavaHostCollection = new ProcessHostCollection(this, xml.SelectSingleNode("processes"));

         ImportLog.Log(_LogType.ltTimer, "loading: endpoints");
         Endpoints = new Endpoints(this, xml.SelectMandatoryNode("endpoints"));

         ImportLog.Log(_LogType.ltTimer, "loading: converters");
         Converters = new Converters(
            xml.SelectSingleNode("converters"),
            "converter",
            (node) => Converter.Create (node),
            false);

         ImportLog.Log(_LogType.ltTimer, "loading: pipelines");
         Pipelines = new NamedAdminCollection<Pipeline>(
            xml.SelectMandatoryNode("pipelines"),
            "pipeline",
            (node) => new Pipeline(this, node),
            true);
         
         ImportLog.Log(_LogType.ltTimer, "loading: datasources");
         Datasources = new NamedAdminCollection<DatasourceAdmin>(
            xml.SelectMandatoryNode("datasources"),
            "datasource",
            (node) => new DatasourceAdmin(ctx, node),
            true);
      
         ImportLog.Log(_LogType.ltTimerStop, "loading: finished");
      }

      private void fillTikaVars()
      {
         String dir = IOUtils.FindDirectoryToRoot(Assembly.GetExecutingAssembly().Location, "TikaService", FindToTootFlags.ReturnNull);
         if (String.IsNullOrEmpty(dir)) return;
         Environment.SetEnvironmentVariable("IMPORT_TIKA_SERVICE", dir);

         String jetty = findLargest(dir, "jetty-runner-*.jar");
         if (jetty == null) return;

         String war = findLargest(Path.Combine(dir, "target"), "tikaservice-*.war");
         if (war == null) return;

         Environment.SetEnvironmentVariable("IMPORT_TIKA_CMD", String.Format("\"{0}\"  \"{1}\"", jetty, war));
      }

      private String findLargest(String dir, String spec)
      {
         String max = null;
         String[] files = Directory.GetFiles(dir, spec);
         foreach (var f in files)
         {
            if (String.Compare(f, max, true) <= 0) continue;
            max = f;
         }
         return max;
      }

      static bool isActive(String[] enabledDSses, DatasourceAdmin da)
      {
         if (enabledDSses == null) return da.Active;
         for (int i = 0; i < enabledDSses.Length; i++)
         {
            if (da.Name.Equals(enabledDSses[i], StringComparison.InvariantCultureIgnoreCase)) return true;
         }
         return false;
      }


      public void Import(String enabledDSses)
      {
         Import(enabledDSses.SplitStandard());
      }
      public void Import(String[] enabledDSses=null)
      {
         StartTimeUtc = DateTime.UtcNow;

         ImportLog.Log();
         ImportLog.Log(_LogType.ltProgress, "Starting import. Flags={0}, MaxAdds={1}, ActiveDS's='{2}'.", ImportFlags, MaxAdds, enabledDSses==null ? null : String.Join (", ", enabledDSses));
         PipelineContext mainCtx = new PipelineContext(this);
         Endpoints.Open(mainCtx);

         try
         {
            for (int i = 0; i < Datasources.Count; i++)
            {
               DatasourceAdmin admin = Datasources[i];
               if (!isActive(enabledDSses, admin))
               {
                  ImportLog.Log(_LogType.ltProgress, "[{0}]: not active", admin.Name);
                  continue;
               }

               PipelineContext ctx = new PipelineContext(this, admin);
               var pipeline = admin.Pipeline;
               ImportLog.Log(_LogType.ltProgress | _LogType.ltTimerStart, "[{0}]: starting import with pipeline {1}, default endpoint={2}, maxadds={3} ", admin.Name, pipeline.Name, pipeline.DefaultEndpoint, ctx.MaxAdds);

               try
               {
                  pipeline.Start(ctx);
                  admin.Datasource.Import(ctx, pipeline);
                  ImportLog.Log(_LogType.ltProgress | _LogType.ltTimerStop, "[{0}]: import ended. {1}.", admin.Name, ctx.GetStats());
               }
               catch (Exception err)
               {
                  if (MaxAddsExceededException.ContainsMaxAddsExceededException (err))
                  {
                     ctx.ErrorState |= _ErrorState.Limited;
                     ImportLog.Log(_LogType.ltWarning | _LogType.ltTimerStop, "[{0}]: {1}", admin.Name, err.Message);
                     ImportLog.Log("-- " + ctx.GetStats());
                     if ((ImportFlags & _ImportFlags.IgnoreLimited) != 0)
                        ImportLog.Log(_LogType.ltWarning, "Limited ignored due to importFlags [{0}].", ImportFlags);
                     else
                        mainCtx.ErrorState |= _ErrorState.Limited;
                  }
                  else
                  {
                     ctx.ErrorState |= _ErrorState.Error;
                     ImportLog.Log(_LogType.ltError | _LogType.ltTimerStop, "[{0}]: crashed err={1}", admin.Name, err.Message);
                     ImportLog.Log("-- " + ctx.GetStats());
                     Exception toThrow = new BMException(err, "{0}\r\nDatasource={1}.", err.Message, admin.Name);
                     ErrorLog.Log(toThrow);
                     if ((ImportFlags & _ImportFlags.IgnoreErrors) != 0)
                        ImportLog.Log(_LogType.ltWarning, "Error ignored due to importFlags [{0}].", ImportFlags);
                     else
                     {
                        mainCtx.ErrorState |= _ErrorState.Error;
                        throw toThrow;
                     }
                  }
               }
               pipeline.Stop(ctx);
               Endpoints.OptClosePerDatasource(ctx);

               foreach (var c in Converters) c.DumpMissed(ctx);
            }
            ImportLog.Log(_LogType.ltProgress, "Import ended");
            JavaHostCollection.StopAll();
            Endpoints.Close(mainCtx);
         }
         finally
         {
            try
            {
               Endpoints.CloseFinally(mainCtx);
               JavaHostCollection.StopAll();
            }
            catch (Exception e2)
            {
               ErrorLog.Log(e2);
               ImportLog.Log(e2);
            }
         }
      }

      private static String replaceKnownTypes(String typeName)
      {
         if (typeName != null)
         {
            switch (typeName.ToLowerInvariant())
            {
               case "endpoint": return typeof(Endpoint).FullName;
               case "esendpoint": return typeof(ESEndpoint).FullName;
               case "csv": return typeof(CsvDatasource).FullName;
            }
         }
         return typeName;
      }

      private static String replaceKnownTypes(XmlNode node)
      {
         return replaceKnownTypes (node.ReadStr("@type"));
      }
      public static T CreateObject<T>(String typeName) where T : class
      {
         return Objects.CreateObject<T>(replaceKnownTypes(typeName));
      }

      public static T CreateObject<T>(String typeName, params Object[] parms) where T : class
      {
         return Objects.CreateObject<T>(replaceKnownTypes(typeName), parms);
      }

      public static T CreateObject<T>(XmlNode node) where T : class
      {
         return Objects.CreateObject<T>(replaceKnownTypes(node));
      }

      public static T CreateObject<T>(XmlNode node, params Object[] parms) where T : class
      {
         return Objects.CreateObject<T>(replaceKnownTypes(node), parms);
      }
   }
}
