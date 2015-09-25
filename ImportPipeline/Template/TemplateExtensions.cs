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
using System.IO;

namespace Bitmanager.ImportPipeline.Template
{
   public static class TemplateExtensions
   {
      public static TextReader ResultAsReader(this ITemplateEngine eng)
      {
         return eng.ResultAsStream().CreateTextReader();
      }

      public static String WriteGeneratedOutput(this ITemplateEngine eng)
      {
         String outputFn = eng.OutputFileName;
         using (var fs = File.Create(outputFn))
         {
            Stream x = eng.ResultAsStream();
            x.Position = 0;
            x.CopyTo(fs);
         }
         return outputFn;
      }
   }

}
