﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using Bitmanager.Core;
using Bitmanager.Xml;
using Newtonsoft.Json.Linq;
using Bitmanager.Json;
using Bitmanager.IO;

namespace Bitmanager.ImportPipeline
{
   public class FileNameFeeder : IDatasourceFeeder
   {
      private XmlNode ctx;
      private FileTree tree;
      private string root;
      private bool recursive;
      private static Logger errLogger = Logs.ErrorLog.Clone("FileNameFeeder");

      private static FeederElementBase createUri(XmlNode ctx, Uri baseUri, String url)
      {
         return new FeederElementBase(ctx, baseUri == null ? new Uri(url) : new Uri(baseUri, url));
      }
      public void Init(PipelineContext ctx, XmlNode node)
      {
         tree = new FileTree();
         this.ctx = node;
         root = XmlUtils.ReadStr(node, "@root");
         recursive = XmlUtils.OptReadBool(node, "@recursive", true);

         String filter = XmlUtils.OptReadStr(node, "@filter", null);

         tree.OnFileError += fileTree_OnFileError;
         if (filter != null) tree.AddFileFilter(filter, true);

         addFilters(tree, node.SelectNodes("dir"), true);
         addFilters(tree, node.SelectNodes("file"), false);
      }
      private static void addFilters(FileTree tree, XmlNodeList nodes, bool isDir)
      {
         foreach (XmlNode node in nodes)
         {
            String incl = XmlUtils.OptReadStr(node, "@incl", null);
            String excl = XmlUtils.OptReadStr(node, "@excl", null);
            if (incl == null && excl == null)
               throw new BMNodeException(node, "At least 1 of the attributes incl or excl must be present.");

            if (incl != null)
            {
               if (isDir) tree.AddDirFilter(incl, true); else tree.AddFileFilter(incl, true);
            }

            if (excl != null)
            {
               if (isDir) tree.AddDirFilter(excl, false); else tree.AddFileFilter(excl, false);
            }
         }
      }

      void fileTree_OnFileError(FileTree sender, FileTree.ErrorArguments args)
      {
         errLogger.Log("Cannot read '{0}': {1}.", args.FileName, args.Error);
         errLogger.Log(args.Error);
         args.Continue = true;
      }

      public IEnumerator<IDatasourceFeederElement> GetEnumerator()
      {
         tree.ReadFiles (root, recursive ? (_ReadFileFlags.rfStoreFiles |  _ReadFileFlags.rfSubdirs) : (_ReadFileFlags.rfStoreFiles));

         for (int i = 0; i < tree.Files.Count; i++)
            yield return new FileNameFeederElement (ctx, tree, tree.Files[i]);
      }

      System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
      {
         return GetEnumerator();
      }
   }

   public class FileNameFeederElement : FeederElementBase
   {
      public readonly String FileName;
      public readonly String RelativeName;

      public FileNameFeederElement(XmlNode ctx, FileTree tree, String relname): base (ctx, tree.GetFullName(relname))
      {
         RelativeName = relname;
         FileName = (String)Element;
      }

   }
}