﻿using Bitmanager.Core;
using Bitmanager.IO;
using Bitmanager.Xml;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;


namespace Bitmanager.Java
{
   public class ProcessHostSettings
   {
      public readonly String ErrorLogName;
      public readonly String LogName;
      public readonly String LogFrom;
      public readonly String ShutdownUrl;
      public readonly String ShutdownMethod;
      public readonly String ExeName;
      public readonly String Arguments;
      public readonly int StartDelay; 
      public readonly int MaxRestarts;
      public readonly bool ClearLogs;

      public ProcessHostSettings(XmlNode node)
      {
         MaxRestarts = node.OptReadInt("@restarts", 25);
         ClearLogs = node.OptReadBool("@clearlogs", true);
         LogName = node.OptReadStr("@log", "console");
         ErrorLogName = node.OptReadStr("@errlog", LogName);
         LogFrom = node.OptReadStr("@logfrom", "console");
         ExeName = node.ReadStr("exe");
         Arguments = node.OptReadStr("arguments", null);
         StartDelay = node.OptReadInt("@startdelay", -1);

         ShutdownUrl = node.OptReadStr("shutdown/@url", null);
         if (ShutdownUrl != null) ShutdownMethod = node.OptReadStr("shutdown/@method", "POST");
      }

   }

   
   
   
   
   //public class Settings
   //{
   //   const String Version = "1.0";

   //   public enum _VarType { Default, Override };
   //   public readonly XmlHelper Xml;
   //   public readonly List<ProcessSettings> Processes;
   //   public readonly String ServiceName;
   //   public bool IsValid { get { return Xml != null; } }

   //   public Settings(String defServiceName, String fn=null, bool optional=false)
   //   {
   //      ServiceName = defServiceName;

   //      String dir = Path.GetDirectoryName (Assembly.GetExecutingAssembly ().Location);
   //      String fullName = IOUtils.FindFileToRoot(dir, String.IsNullOrEmpty(fn) ? "settings.xml" : fn, FindToTootFlags.ReturnOriginal);

   //      if (optional && !File.Exists (fullName)) return;

   //      Xml = new XmlHelper(fullName);
   //      Xml.CheckVersion(Version);
   //      ServiceName = Xml.OptReadStr("service/@name", defServiceName);

   //      XmlNodeList processNodes = Xml.SelectNodes("service/process");
   //      Processes = new List<ProcessSettings>(processNodes.Count);
   //      for (int i=0; i<processNodes.Count; i++)
   //         Processes.Add (new ProcessSettings (this, processNodes[i]));


   //      XmlNodeList vars = Xml.SelectNodes("variables/variable");
   //      foreach (XmlNode v in vars)
   //      {
   //         String name = XmlUtils.ReadStr(v, "@name");
   //         String value = XmlUtils.OptReadStr(v, "@value", String.Empty);
   //         _VarType vt = XmlUtils.OptReadEnum (v, "@type", _VarType.Override);
   //         if (vt== _VarType.Default)
   //         {
   //            if (!String.IsNullOrEmpty (Environment.GetEnvironmentVariable (name))) 
   //               continue;
   //         }
   //         Environment.SetEnvironmentVariable (name, value);
   //      }

   //      //foreach (DictionaryEntry kvp in Environment.GetEnvironmentVariables())
   //      //{
   //      //   String name = (String)kvp.Key;
   //      //   if (Variables.ContainsKey(name)) continue;
   //      //   Variables.Add(name, Environment.ExpandEnvironmentVariables((String)kvp.Value));
   //      //}

   //   }
   //}
}