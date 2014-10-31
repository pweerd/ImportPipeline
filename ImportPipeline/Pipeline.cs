﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bitmanager.Core;
using Bitmanager.Xml;
using System.Xml;
using Newtonsoft.Json.Linq;
using Bitmanager.Json;

namespace Bitmanager.ImportPipeline
{
   class ActionAdmin
   {
      public readonly String Key;
      public readonly PipelineAction Action;
      public readonly int KeyLen;
      public readonly int Order;

      //Next fields are filled by the pipeline when sorting
      public int  Index;
      public int  EqualityID;
      public bool EqualToPrev;

      public ActionAdmin(String key, int order, PipelineAction action)
      {
         this.Key = key.ToLowerInvariant();
         this.KeyLen = this.Key.Length;
         this.Order = order;
         this.Action = action;
      }
   }

   public class Pipeline : NamedItem, IDatasourceSink
   {
      public const String ItemStart = "_item/_start";
      public const String ItemStop = "_item/_stop";
      private Dictionary<String, Object> variables;
      private StringDict<IDataEndpoint> endPointCache;

      public readonly String DefaultEndpoint;
      public readonly String DefaultConverters;
      public readonly ImportEngine ImportEngine;
      public readonly String ScriptTypeName;

      public Object ScriptObject { get; private set; }

      internal bool trace;
      private bool started;
      /// <summary>
      /// List of active Actions (only valid in a running pipeline)
      /// </summary>
      internal List<ActionAdmin> actions;

      /// <summary>
      /// List of defined Actions. Unmutable.
      /// </summary>
      internal List<ActionAdmin> definedActions;
      
      internal List<PipelineTemplate> templates;
      internal Logger logger;

      StringDict missed;

      public Pipeline(ImportEngine engine, XmlNode node): base(node)
      {
         ImportEngine = engine;
         logger = engine.DebugLog.Clone ("pipeline");

         ScriptTypeName = node.OptReadStr("@script", null);
         DefaultConverters = node.OptReadStr("@converters", null);
         DefaultEndpoint = node.OptReadStr("@endpoint", null);
         if (DefaultEndpoint == null)
         {
            if (engine.Endpoints.Count == 1)
               DefaultEndpoint = engine.Endpoints[0].Name;
            else if (engine.Endpoints.OptGetByName(Name) != null)
               DefaultEndpoint = Name;
         }
         trace = node.OptReadBool ("@trace", false);

         AdminCollection<PipelineAction> rawActions = new AdminCollection<PipelineAction>(node, "action", (x) => PipelineAction.Create(this, x), false);
         definedActions = new List<ActionAdmin>();
         for (int i = 0; i < rawActions.Count; i++)
         {
            var action = rawActions[i];
            String[] keys = action.Name.SplitStandard();
            for (int k = 0; k < keys.Length; k++)
               definedActions.Add(new ActionAdmin(keys[k], i, action));
         }
         definedActions.Sort(cbSortAction);

         var templNodes = node.SelectNodes("template");
         templates = new List<PipelineTemplate>(templNodes.Count);
         for (int i = 0; i < templNodes.Count; i++)
         {
            templates.Add (PipelineTemplate.Create (this, templNodes[i]));
         }

         Dump("");
      }

      public void SetVariable(String varName, Object value)
      {
         if (variables == null) variables = new Dictionary<string, object>();
         variables[varName.ToLowerInvariant()] = value;
      }
      public Object GetVariable(String varName)
      {
         if (variables == null) return null;
         Object ret;
         if (variables.TryGetValue(varName.ToLowerInvariant(), out ret)) return ret;
         return null;
      }
      public String GetVariableStr(String varName)
      {
         if (variables == null) return null;
         Object ret;
         if (variables.TryGetValue(varName.ToLowerInvariant(), out ret)) return ret.ToString();
         return null;
      }

      public void ClearVariables()
      {
         variables = null;
      }
      public void ClearVariables(String[] varsToClear)
      {
         if (variables==null || varsToClear==null) return;
         for (int i = 0; i < varsToClear.Length; i++)
         {
            variables.Remove(varsToClear[i]);
         }
      }

      private static String[] splitEndpoint(String s)
      {
         if (String.IsNullOrEmpty(s)) return null;
         String[] parts = s.Split('.');
         for (int i = 0; i < parts.Length; i++)
            parts[i] = parts[i].TrimToNull();
         if (parts.Length >= 3) return parts;

         String[] parts3 = new String[3];
         Array.Copy (parts, parts3, parts.Length);
         return parts3;
      }

      public void Start(PipelineContext ctx)
      {
         if (trace) ctx.ImportFlags |= _ImportFlags.TraceValues;
         logger.Log("Starting datasource {0}", ctx.DatasourceAdmin.Name);
         missed = new StringDict();
         if (ScriptTypeName != null && ScriptObject==null)
         {
            ScriptObject = Objects.CreateObject(ScriptTypeName, ctx);
            logger.Log("XScript({0})={1}", ScriptTypeName, ScriptObject);
         }

         //Clone the list of actions and strat them
         actions = new List<ActionAdmin>(definedActions.Count);
         for (int i = 0; i < definedActions.Count; i++)
         {
            ActionAdmin act = definedActions[i];
            act.Action.Start(ctx);
            actions.Add(act);
         }
         prepareActions();

         if (endPointCache != null)
            foreach (var kvp in this.endPointCache)
               kvp.Value.Start(ctx);

         started = true;
         HandleValue(ctx, "_datasource/_start", ctx.DatasourceAdmin.Name);
      }

      public void Stop(PipelineContext ctx)
      {
         HandleValue(ctx, "_datasource/_stop", ctx.DatasourceAdmin.Name);
         ctx.MissedLog.Log("Stopped datasource [{0}]. {1} missed keys.", ctx.DatasourceAdmin.Name, missed.Count);
         foreach (var kvp in missed)
         {
            ctx.MissedLog.Log("-- {0}", kvp.Key);
         }
         missed = new StringDict();

         started = false;
         if (endPointCache != null)
            foreach (var kvp in this.endPointCache)
               kvp.Value.Stop(ctx);
         ctx.LogLastAdd();
         Dump("after import");

         endPointCache = null;
         actions = null;
      }

      public IDataEndpoint GetDataEndpoint(PipelineContext ctx, String name)
      {
         IDataEndpoint ret;
         if (name == null) name = String.Empty;
         if (endPointCache == null) endPointCache = new StringDict<IDataEndpoint>();
         if (endPointCache.TryGetValue(name, out ret)) return ret;

         ret = this.ImportEngine.Endpoints.GetDataEndpoint(ctx, name);
         endPointCache.Add(name, ret);
         if (started) ret.Start(ctx); 
         return ret;
      }


      public Object HandleValue(PipelineContext ctx, String key, Object value)
      {
         Object orgValue = value;
         Object ret = null;
         Object lastAction = null;
         try
         {
            ctx.ActionFlags = 0;

            if ((ctx.ImportFlags & _ImportFlags.TraceValues) != 0) logger.Log("HandleValue ({0}, {1} [{2}]", key, value, value == null ? "null" : value.GetType().Name);

            if (key == null) goto EXIT_RTN;
            String lcKey = key.ToLowerInvariant();
            int keyLen = lcKey.Length;

            if (ctx.SkipUntilKey != null)
            {
               ctx.ActionFlags |= _ActionFlags.Skipped;
               if (ctx.SkipUntilKey.Length == keyLen && lcKey.Equals(ctx.SkipUntilKey, StringComparison.InvariantCultureIgnoreCase))
                  ctx.SkipUntilKey = null;
               goto EXIT_RTN;
            }

            int ixStart = findAction(lcKey);
            if (ixStart < 0)
            {
               if (templates.Count == 0 || !checkTemplates(ctx, key, ref lastAction)) //templates==0: otherwise checkTemplates() inserts a NOP action...
               {
                  missed[lcKey] = null;
                  goto EXIT_RTN;
               }
               ixStart = findAction(lcKey);
               if (ixStart < 0) goto EXIT_RTN;  //Should not happen, just to be sure!
            }

            for (int i = ixStart; i < actions.Count; i++)
            {
               ActionAdmin a = actions[i];
               if (i > ixStart && !a.EqualToPrev) break;

               lastAction = ctx.SetAction(a.Action);
               Object tmp = a.Action.HandleValue(ctx, key, value);
               ClearVariables(a.Action.VarsToClear);
               if (tmp != null) ret = tmp;
               if ((ctx.ActionFlags & _ActionFlags.SkipRest) != 0)
                  break;
            }

            EXIT_RTN: return ret;
         }
         catch (Exception e)
         {
            String type;
            if (orgValue == value)
               type = String.Format("[{0}]", getType(orgValue));
            else
               type = String.Format("[{0}] (was [{1}])", getType(value), getType(orgValue));
            ctx.ErrorLog.Log("Exception while handling event. Key={0}, value type={1}, action={2}", key, type, lastAction);
            ctx.ErrorLog.Log("-- value={0}", value);
            if (orgValue != value)
               ctx.ErrorLog.Log("-- orgvalue={0}", orgValue);

            ctx.ErrorLog.Log(e);
            PipelineAction act = lastAction as PipelineAction;
            if (act == null)
               ctx.ErrorLog.Log("Cannot dump accu: no current action found.");
            else
            {
               var accu = (JObject)act.Endpoint.GetFieldAsToken(null);
               ctx.ErrorLog.Log("Dumping content of current accu: fieldcount={0}", accu.Count);
               ctx.ErrorLog.Log(act.Endpoint.GetFieldAsToken(null).ToString());
            }

            throw new BMException (e, "{0}\r\nKey={1}, valueType={2}.", e.Message, key, type);
         }
      }

      private static String getType(Object obj)
      {
         if (obj == null) return "null";
         JValue jv = obj as JValue;
         return jv == null ? obj.GetType().Name : jv.Type.ToString();
      }

      Dictionary<String, ActionAdmin> actionDict; 
      private void prepareActions()
      {
         actionDict = new Dictionary<string, ActionAdmin>(actions.Count);
         if (actions.Count==0) return;
         int equalityID = 0;
         actions.Sort(cbSortAction);
         ActionAdmin prev = actions[0];
         actionDict.Add(prev.Key, prev);
         prev.EqualToPrev = false;
         prev.Index       = 0;
         prev.EqualityID = equalityID;
         for (int i=1; i<actions.Count; i++)
         {
            ActionAdmin a = actions[i];
            a.Index = i;
            a.EqualityID = equalityID;
            a.EqualToPrev = (a.Key == prev.Key);
            if (a.EqualToPrev) continue;
            prev = a;
            a.EqualityID = ++equalityID;
            actionDict.Add(a.Key, a);
         }
      }

      private bool checkTemplates(PipelineContext ctx, String key, ref Object lastTemplate)
      {
         PipelineAction a = null;
         String templateExpr = null;
         int i;
         for (i = 0; i < templates.Count; i++)
         {
            lastTemplate = templates[i];
            a = templates[i].OptCreateAction(ctx, key);
            if (a != null) goto ADD_TEMPLATE;
         }
         a = new PipelineNopAction (key);
         actions.Add(new ActionAdmin(a.Name, actions.Count, a));
         prepareActions();
         return false;

      ADD_TEMPLATE:
         templateExpr = templates[i].Expr;
         while (true)
         {
            a.Start(ctx);
            actions.Add(new ActionAdmin(a.Name, actions.Count, a));
            i++;
            if (i >= templates.Count) break;
            if (!templates[i].Expr.Equals(templateExpr, StringComparison.InvariantCultureIgnoreCase)) break;
            a = templates[i].OptCreateAction(ctx, key);
            if (a == null) break;
         }
         prepareActions();
         return true;
      }

      private int findAction(String key)
      {
         int kl = key.Length;
         for (int i = 0; i < actions.Count; i++)
         {
            ActionAdmin a = actions[i];
            if (a.KeyLen < kl) continue;
            if (a.KeyLen > kl) return -1;
            int rc = String.CompareOrdinal(a.Key, key);
            if (rc < 0) continue;
            if (rc > 0) return -1;

            return i;
         }
         return -1;
      }

      private static int cbSortAction(ActionAdmin left, ActionAdmin right)
      {
         var intComparer = Comparer<int>.Default;
         int rc = intComparer.Compare(left.KeyLen, right.KeyLen);
         if (rc != 0) return rc;

         rc = String.CompareOrdinal(left.Key, right.Key);
         if (rc != 0) return rc;

         return intComparer.Compare(left.Order, right.Order);
      }


      public void Dump (String why)
      {
         logger.Log("Dumping pipeline {0} {1}", Name, why);
         var list = actions == null ? definedActions : actions; 

         logger.Log("-- {0} actions", list.Count);
         for (int i = 0; i < list.Count; i++)
         {
            var action = list[i];
            logger.Log("-- -- action order={0} {1}", action.Order, action.Action);
         }

         logger.Log("-- {0} templates", templates.Count);
         for (int i = 0; i < templates.Count; i++)
         {
            logger.Log("-- -- " + templates[i]);
         }
      }

      public bool HandleException(PipelineContext ctx, string prefix, Exception err)
      {
         String pfx = String.IsNullOrEmpty(prefix) ? "_error" : prefix + "/_error";
         HandleValue(ctx, pfx + "/date", DateTime.UtcNow);
         HandleValue(ctx, pfx + "/msg", err.Message);
         HandleValue(ctx, pfx + "/trace", err.StackTrace);
         HandleValue(ctx, pfx, err);
         return (ctx.ActionFlags & _ActionFlags.Handled) != 0;
      }

      public static void EmitToken(PipelineContext ctx, IDatasourceSink sink, JToken token, String key, int maxLevel)
      {
         if (token == null) return;
         Object value = token;
         maxLevel--; 
         switch (token.Type)
         {
            case JTokenType.Array:
               if (maxLevel < 0) break;
               var arr = (JArray)token;
               String tmpKey = key + "/_v";
               for (int i=0; i<arr.Count; i++)
                  EmitToken(ctx, sink, arr[i], tmpKey, maxLevel);
               sink.HandleValue(ctx, key, null);
               return;
            case JTokenType.None:
            case JTokenType.Null:
            case JTokenType.Undefined:
               value = null;
               break;
            case JTokenType.Date: value = (DateTime)token; break;
            case JTokenType.String: value = (String)token; break;
            case JTokenType.Float: value = (double)token; break;
            case JTokenType.Integer: value = (Int64)token; break;
            case JTokenType.Boolean: value = (bool)token; break;

            case JTokenType.Object:
               if (maxLevel < 0) break;
               JObject obj = (JObject)token;
               int newLvl = maxLevel - 1;
               foreach (var kvp in obj)
               {
                  EmitToken (ctx, sink, kvp.Value, key + "/" + generateObjectKey(kvp.Key), maxLevel);
               }
               sink.HandleValue(ctx, key, null);
               return;
         }
         sink.HandleValue(ctx, key, value);
      }
      static private String generateObjectKey(String k)
      {
         return String.IsNullOrEmpty(k) ? "_o" : k;
      }
   }




}
