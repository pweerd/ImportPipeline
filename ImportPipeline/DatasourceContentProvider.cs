﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;

namespace Bitmanager.ImportPipeline
{
   public interface IDatasourceContentProvider : IEnumerable<Object>
   {
      void Init(PipelineContext ctx, XmlNode node);
   }
}
