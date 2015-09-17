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
   
