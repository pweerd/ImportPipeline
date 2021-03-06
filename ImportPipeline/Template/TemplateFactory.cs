﻿/*
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
using Bitmanager.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Bitmanager.ImportPipeline.Template
{
   public interface ITemplateFactory
   {
      IVariables InitialVariables { get; set; }
      int DebugLevel { get; set; }
      bool AutoWriteGenerated { get; set; }
      ITemplateEngine CreateEngine();
   }



   public interface IVariables : IEnumerable<KeyValuePair<String, Object>>
   {
      int Count { get; }
      void Clear();
      void Set(string key, Object value);
      Object Get(string key);
      IVariables Clone();
   }


   public interface ITemplateEngine
   {
      String FileName { get; set; }
      String OutputFileName { get; } 
      IVariables Variables { get; set; }
      IVariables FileVariables { get ;  }
      void LoadFromFile (String fn);
      Stream ResultAsStream();
   }

}
