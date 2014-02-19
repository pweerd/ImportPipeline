using Bitmanager.Core;
//using Bitmanager.Elastic;
using Bitmanager.Xml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using Bitmanager.ImportPipeline;

   public class Gorilla
   {
      private String name;
      private String exchange;

      public Object OnName (PipelineContext ctx, String key, Object value)
      {
         if (value == null) return null;
         name = value.ToString().ToLowerInvariant();
         if (exchange != null) emitKey(ctx);
         return value;
      }
      public Object OnExchange (PipelineContext ctx, String key, Object value)
      {
         if (value == null) return null;
         exchange = value.ToString().ToLowerInvariant();
         if (name != null) emitKey(ctx);
         return value;
      }
      
      private void emitKey(PipelineContext ctx)
      {
         ctx.Pipeline.HandleValue (ctx, "record/key", String.Format ("{0}[{1}]", name, exchange));
      }
      
      public Object Clear (PipelineContext ctx, String key, Object value)
      {
         name = null;
         exchange = null;
         return value;
      }
   }
   