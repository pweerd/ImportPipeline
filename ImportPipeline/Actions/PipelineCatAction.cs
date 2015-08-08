﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bitmanager.Core;
using Bitmanager.Xml;
using System.Xml;
using Newtonsoft.Json.Linq;
using Bitmanager.Json;
using System.Text.RegularExpressions;
using Bitmanager.Elastic;

namespace Bitmanager.ImportPipeline
{
   public class PipelineCategorieAction : PipelineAction
   {
      public CategoryCollection[] Categories;
      public PipelineCategorieAction(Pipeline pipeline, XmlNode node)
         : base(pipeline, node)
      {
          Categories = loadCategories(pipeline, node);
      }

      internal PipelineCategorieAction(PipelineCategorieAction template, String name, Regex regex)
         : base(template, name, regex)
      {
         Categories = template.Categories;
      }

      static CategoryCollection[] loadCategories(Pipeline pipeline, XmlNode node)
      {
         String[] cats = node.ReadStr("@categories").SplitStandard();

         CategoryCollection[] list = new CategoryCollection[cats.Length];
         var engine = pipeline.ImportEngine;
         for (int i = 0; i < cats.Length; i++)
         {
            list[i] = engine.Categories.GetByName(cats[i]);
         }
         return list;
      }

      public override Object HandleValue(PipelineContext ctx, String key, Object value)
      {
         value = ConvertAndCallScript(ctx, key, value);
         if ((ctx.ActionFlags & _ActionFlags.Skip) != 0) goto EXIT_RTN;

         foreach (var cat in Categories) cat.HandleRecord(ctx);

      EXIT_RTN:
         return PostProcess(ctx, value);
      }
   }

   public class PipelineCategorieTemplate : PipelineTemplate
   {
      public PipelineCategorieTemplate(Pipeline pipeline, XmlNode node)
         : base(pipeline, node)
      {
         template = new PipelineCategorieAction(pipeline, node);
      }

      public override PipelineAction OptCreateAction(PipelineContext ctx, String key)
      {
         if (!regex.IsMatch(key)) return null;
         return new PipelineCategorieAction((PipelineCategorieAction)template, key, regex);
      }
   }


}