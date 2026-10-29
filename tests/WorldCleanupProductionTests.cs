using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Extracts and executes the native objGenScript.categoryClass.UpdGen cleanup
// region. Only the initial generation work is omitted; the IL from
// chunksToRemove.Clear() through the native return is copied verbatim, with
// fields and game-owned nested types mapped to small data-only stand-ins.
public static class WorldCleanupProductionTests
{
    static ModuleDefinition target;
    static readonly Dictionary<string,TypeDefinition> localTypes = new Dictionary<string,TypeDefinition>();
    static int checks;

    public static int Main(string[] args)
    {
        try
        {
            if(args.Length<3)throw new Exception("Usage: WorldCleanupProductionTests ASSEMBLY-CSharp.dll MANAGED-DIR baseline|candidate [chunks] [needed] [rounds]");
            int chunks=args.Length>3?Int32.Parse(args[3]):5000;
            int needed=args.Length>4?Int32.Parse(args[4]):2000;
            int rounds=args.Length>5?Int32.Parse(args[5]):3;
            if(chunks<1 || needed<0 || needed>chunks || rounds<1)throw new Exception("Invalid benchmark sizes");
            string self=Assembly.GetExecutingAssembly().Location;
            string output=Path.Combine(Path.GetTempPath(),"NativeCleanupExtracted-"+Guid.NewGuid().ToString("N")+".dll");
            using(var fixture=AssemblyDefinition.ReadAssembly(self))
            using(var source=AssemblyDefinition.ReadAssembly(args[0]))
            {
                target=fixture.MainModule;
                IndexLocalTypes(target.Types);
                var sourceCategory=AllTypes(source.MainModule.Types).Single(t=>t.FullName=="objGenScript/categoryClass");
                var src=sourceCategory.Methods.Single(m=>m.Name=="UpdGen" && m.Parameters.Count==1);
                var dst=target.Types.Single(t=>t.FullName=="objGenScript").NestedTypes.Single(t=>t.Name=="categoryClass");
                DefineNativeFields(dst,sourceCategory);
                CopyCleanup(src,dst.Methods.Single(m=>m.Name=="Cleanup"),args[2]=="candidate");
                fixture.Name.Name="NativeCleanupExtracted"+Guid.NewGuid().ToString("N");
                fixture.Write(output);
            }
            AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs e)
            {
                string name=new AssemblyName(e.Name).Name;
                string path=name=="Assembly-CSharp"?args[0]:Path.Combine(args[1],name+".dll");
                return File.Exists(path)?Assembly.LoadFrom(path):null;
            };
            var native=Assembly.LoadFrom(args[0]);
            var assembly=Assembly.LoadFrom(output);
            var category=assembly.GetType("objGenScript+categoryClass",true);
            var vector=native.GetType("UnityEngine.Vector3d",true);
            var chunk=native.GetType("objGenScript+chunkClass",true);
            var obj=native.GetType("objGenScript+objClass",true);
            var method=category.GetMethod("Cleanup",BindingFlags.Instance|BindingFlags.Public);
            string parity=FingerprintHash(RunCases(category,vector,chunk,obj,method,chunks,needed)+"|edge="+RunEdgeCases(category,vector,chunk,obj,method));
            long ticks=Benchmark(category,vector,chunk,obj,method,chunks,needed,rounds);
            Console.WriteLine("PASS production UpdGen cleanup IL role="+args[2]+" checks="+checks+" parity="+parity+" chunks="+chunks+" needed="+needed+" rounds="+rounds+" cleanupTicks="+ticks+" (synthetic; not a game FPS claim)");
            return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }

    static void IndexLocalTypes(IEnumerable<TypeDefinition> types)
    {foreach(var t in types){if(t.FullName=="objGenScript/categoryClass")localTypes.Add(t.FullName,t);IndexLocalTypes(t.NestedTypes);}}

    static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
    {foreach(var t in types){yield return t;foreach(var child in AllTypes(t.NestedTypes))yield return child;}}

    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}

    static void DefineNativeFields(TypeDefinition targetCategory,TypeDefinition sourceCategory)
    {
        targetCategory.Fields.Clear();
        foreach(string name in new[]{"chunksNeeded","chunksToRemove","generatedChunks","count"})
        {
            var sourceField=sourceCategory.Fields.Single(f=>f.Name==name);
            targetCategory.Fields.Add(new FieldDefinition(name,Mono.Cecil.FieldAttributes.Public,MapType(sourceField.FieldType)));
        }
    }

    static void CopyCleanup(MethodDefinition source,MethodDefinition targetMethod,bool candidate)
    {
        var instructions=source.Body.Instructions;
        int start=-1;
        for(int i=0;i<instructions.Count;i++)
        {
            var call=instructions[i].Operand as MethodReference;
            var field=i>0?instructions[i-1].Operand as FieldReference:null;
            if(call!=null && call.Name=="Clear" && field!=null && field.Name=="chunksToRemove")
            {start=i-2;break;}
        }
        if(start<0)throw new Exception("Native cleanup anchor changed: chunksToRemove.Clear not found");
        int end=instructions.Count-1;
        while(end>=start && instructions[end].OpCode!=OpCodes.Ret)end--;
        if(end<start || end!=instructions.Count-1)throw new Exception("Expected native UpdGen cleanup to continue through its final return");
        int listContains=0,setContains=0,setCtor=0;
        for(int i=start;i<=end;i++)
        {
            var method=instructions[i].Operand as MethodReference;
            if(method==null)continue;
            if(method.Name=="Contains" && method.DeclaringType.FullName.StartsWith("System.Collections.Generic.List`1",StringComparison.Ordinal))listContains++;
            if(method.Name=="Contains" && method.DeclaringType.FullName.StartsWith("System.Collections.Generic.HashSet`1",StringComparison.Ordinal))setContains++;
            if(method.Name==".ctor" && method.DeclaringType.FullName.StartsWith("System.Collections.Generic.HashSet`1",StringComparison.Ordinal))setCtor++;
        }
        if(candidate)
        {
            if(listContains!=0 || setContains!=1 || setCtor!=1)throw new Exception("Candidate cleanup shape changed: expected one local HashSet and one HashSet.Contains, found list="+listContains+" set="+setContains+" ctor="+setCtor);
        }
        else if(listContains!=1 || setContains!=0 || setCtor!=0)
            throw new Exception("Baseline cleanup shape changed: expected one List.Contains, found list="+listContains+" set="+setContains+" ctor="+setCtor);

        targetMethod.Body=new Mono.Cecil.Cil.MethodBody(targetMethod){InitLocals=source.Body.InitLocals,MaxStackSize=source.Body.MaxStackSize};
        var usedVariables=new Dictionary<VariableDefinition,VariableDefinition>();
        for(int i=start;i<=end;i++)
        {
            var old=instructions[i].Operand as VariableDefinition;
            if(old!=null && !usedVariables.ContainsKey(old))
            {var v=new VariableDefinition(MapType(old.VariableType));usedVariables.Add(old,v);targetMethod.Body.Variables.Add(v);}
        }
        var map=new Dictionary<Instruction,Instruction>();
        for(int i=start;i<=end;i++){var old=instructions[i];var copy=Instruction.Create(OpCodes.Nop);copy.OpCode=old.OpCode;targetMethod.Body.Instructions.Add(copy);map.Add(old,copy);}
        for(int i=start;i<=end;i++)map[instructions[i]].Operand=MapOperand(instructions[i].Operand,usedVariables,map,start,end);
        Check(targetMethod.Body.Instructions.Count>0,"No cleanup instructions copied");
    }

    static TypeReference MapType(TypeReference type)
    {
        var gp=type as GenericParameter;if(gp!=null)return gp;
        var by=type as ByReferenceType;if(by!=null)return new ByReferenceType(MapType(by.ElementType));
        var array=type as ArrayType;if(array!=null)return new ArrayType(MapType(array.ElementType),array.Rank);
        var generic=type as GenericInstanceType;
        if(generic!=null){var g=new GenericInstanceType(MapType(generic.ElementType));foreach(var a in generic.GenericArguments)g.GenericArguments.Add(MapType(a));return g;}
        TypeDefinition local;
        if(localTypes.TryGetValue(type.FullName,out local))return local;
        return target.ImportReference(type);
    }

    static object MapOperand(object operand,Dictionary<VariableDefinition,VariableDefinition> variables,Dictionary<Instruction,Instruction> map,int start,int end)
    {
        if(operand==null)return null;
        var branch=operand as Instruction;
        if(branch!=null){if(!map.ContainsKey(branch))throw new Exception("Native cleanup branch escaped extracted region");return map[branch];}
        var branches=operand as Instruction[];
        if(branches!=null){return branches.Select(i=>{if(!map.ContainsKey(i))throw new Exception("Native cleanup switch escaped extracted region");return map[i];}).ToArray();}
        var variable=operand as VariableDefinition;if(variable!=null)return variables[variable];
        var field=operand as FieldReference;
        if(field!=null)
        {
            TypeDefinition local;
            if(localTypes.TryGetValue(field.DeclaringType.FullName,out local))
            {var matches=local.Fields.Where(f=>f.Name==field.Name).ToArray();if(matches.Length!=1)throw new Exception("Missing fixture field for production field: "+field.FullName);return matches[0];}
            if(field.DeclaringType.FullName=="objGenScript/categoryClass")
            {var category=localTypes["objGenScript/categoryClass"];return category.Fields.Single(f=>f.Name==field.Name);}
            return target.ImportReference(field);
        }
        var method=operand as MethodReference;
        if(method!=null)
        {
            var gm=method as GenericInstanceMethod;
            if(gm!=null){var mapped=new GenericInstanceMethod((MethodReference)MapOperand(gm.ElementMethod,variables,map,start,end));foreach(var a in gm.GenericArguments)mapped.GenericArguments.Add(MapType(a));return mapped;}
            TypeDefinition local;
            if(localTypes.TryGetValue(method.DeclaringType.FullName,out local))
            {var found=local.Methods.Where(m=>m.Name==method.Name && m.Parameters.Count==method.Parameters.Count).ToArray();if(found.Length!=1)throw new Exception("Missing fixture method for production method: "+method.FullName);return found[0];}
            return target.ImportReference(method);
        }
        var type=operand as TypeReference;if(type!=null)return MapType(type);
        return operand;
    }

    static string RunCases(Type categoryType,Type vectorType,Type chunkType,Type objType,MethodInfo cleanup,int chunks,int needed)
    {
        var category=Activator.CreateInstance(categoryType);
        var keys=(System.Collections.IList)Activator.CreateInstance(categoryType.GetField("chunksNeeded").FieldType);categoryType.GetField("chunksNeeded").SetValue(category,keys);
        var remove=(System.Collections.IList)Activator.CreateInstance(categoryType.GetField("chunksToRemove").FieldType);categoryType.GetField("chunksToRemove").SetValue(category,remove);
        var dict=(System.Collections.IDictionary)Activator.CreateInstance(categoryType.GetField("generatedChunks").FieldType);categoryType.GetField("generatedChunks").SetValue(category,dict);
        var vectorCtor=vectorType.GetConstructor(new[]{typeof(double),typeof(double),typeof(double)});
        var neededKeys=new List<object>();
        var expectedRemove=new List<object>();
        var expectedNeededObjects=new List<object>();
        var expectedOutsideObjects=new List<object>();
        for(int i=0;i<chunks;i++)
        {
            object key=vectorCtor.Invoke(new object[]{(double)i,0.0,(double)(i%31)});
            var chunk=Activator.CreateInstance(chunkType);
            var objs=(System.Collections.IDictionary)chunkType.GetField("objs").GetValue(chunk);
            if(i<needed)
            {
                keys.Add(key);if(i==2)keys.Add(key);
                var value=Activator.CreateInstance(objType);objType.GetField("place").SetValue(value,true);
                objs.Add(vectorCtor.Invoke(new object[]{(double)(chunks+i),1.0,0.0}),value);expectedNeededObjects.Add(value);
            }
            else
            {
                var value=Activator.CreateInstance(objType);objType.GetField("place").SetValue(value,true);
                objs.Add(vectorCtor.Invoke(new object[]{(double)(chunks+i),1.0,0.0}),value);expectedOutsideObjects.Add(value);
            }
            dict.Add(key,chunk);
            if(i>=needed)expectedRemove.Add(key);
        }
        remove.Add(vectorCtor.Invoke(new object[]{-900001.0,3.0,7.0}));
        cleanup.Invoke(category,null);
        Check(remove.Count==expectedRemove.Count,"Native removal queue count differs from expected all-key scan: actual="+remove.Count+" expected="+expectedRemove.Count);
        for(int i=0;i<expectedRemove.Count;i++)Check(expectedRemove[i].Equals(remove[i]),"Native removal queue order/value differs at "+i);
        Check(dict.Count==needed,"Only needed chunks should remain when outside objects are null");
        for(int i=0;i<needed;i++)
        {
            object key=keys[i];var chunk=dict[key];var objs=(System.Collections.IDictionary)chunkType.GetField("objs").GetValue(chunk);
            foreach(System.Collections.DictionaryEntry entry in objs)Check((bool)objType.GetField("place").GetValue(entry.Value),"Needed chunk object placement flag changed");
        }
        foreach(var value in expectedNeededObjects)Check((bool)objType.GetField("place").GetValue(value),"Needed chunk object placement flag changed");
        foreach(var value in expectedOutsideObjects)Check(!(bool)objType.GetField("place").GetValue(value),"Outside object must be visited and marked for unplace before empty chunk removal");
        return Fingerprint(remove,dict,chunkType,objType)+"|neededFlags="+expectedNeededObjects.Count+"/true|outsideFlags="+expectedOutsideObjects.Count+"/false";
    }

    static string Fingerprint(System.Collections.IList removed,System.Collections.IDictionary dict,Type chunkType,Type objType)
    {
        var text=new System.Text.StringBuilder();text.Append("r=");foreach(var v in removed)text.Append(v).Append(';');text.Append("|d=");
        foreach(System.Collections.DictionaryEntry e in dict){text.Append(e.Key).Append(':');var objs=(System.Collections.IDictionary)chunkType.GetField("objs").GetValue(e.Value);foreach(System.Collections.DictionaryEntry o in objs)text.Append(o.Key).Append('=').Append(objType.GetField("place").GetValue(o.Value)).Append(',');text.Append(';');}
        return text.ToString();
    }

    static string RunEdgeCases(Type categoryType,Type vectorType,Type chunkType,Type objType,MethodInfo cleanup)
    {
        object category=Activator.CreateInstance(categoryType);
        var needed=(System.Collections.IList)Activator.CreateInstance(categoryType.GetField("chunksNeeded").FieldType);categoryType.GetField("chunksNeeded").SetValue(category,needed);
        var removed=(System.Collections.IList)Activator.CreateInstance(categoryType.GetField("chunksToRemove").FieldType);categoryType.GetField("chunksToRemove").SetValue(category,removed);
        var chunks=(System.Collections.IDictionary)Activator.CreateInstance(categoryType.GetField("generatedChunks").FieldType);categoryType.GetField("generatedChunks").SetValue(category,chunks);
        var ctor=vectorType.GetConstructor(new[]{typeof(double),typeof(double),typeof(double)});
        object minusZero=ctor.Invoke(new object[]{-0.0,2.0,3.0});
        object plusZero=ctor.Invoke(new object[]{0.0,2.0,3.0});
        object nan=ctor.Invoke(new object[]{Double.NaN,4.0,5.0});
        object outsider=ctor.Invoke(new object[]{70.0,8.0,9.0});
        needed.Add(minusZero);needed.Add(nan);needed.Add(nan);
        AddChunk(chunks,chunkType,objType,plusZero,true);
        AddChunk(chunks,chunkType,objType,nan,true);
        AddChunk(chunks,chunkType,objType,outsider,false);
        cleanup.Invoke(category,null);
        Check(chunks.Count==2,"Signed-zero and NaN needed keys must remain under native Vector3d equality");
        Check(chunks.Contains(plusZero) && chunks.Contains(nan),"Special needed keys were not found by production HashSet/List membership");
        Check(removed.Count==1 && outsider.Equals(removed[0]),"Special-value case removal order/value changed");
        return Fingerprint(removed,chunks,chunkType,objType);
    }

    static void AddChunk(System.Collections.IDictionary chunks,Type chunkType,Type objType,object key,bool place)
    {
        object chunk=Activator.CreateInstance(chunkType);
        var objs=(System.Collections.IDictionary)chunkType.GetField("objs").GetValue(chunk);
        object value=Activator.CreateInstance(objType);objType.GetField("place").SetValue(value,place);
        objs.Add(key,value);chunks.Add(key,chunk);
    }

    static string FingerprintHash(string value)
    {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","");}

    static long Benchmark(Type categoryType,Type vectorType,Type chunkType,Type objType,MethodInfo cleanup,int chunks,int needed,int rounds)
    {
        var actions=new List<Action>();
        for(int r=0;r<rounds;r++)
        {
            object instance=BuildBenchmarkCase(categoryType,vectorType,chunkType,objType,chunks,needed);
            actions.Add((Action)Delegate.CreateDelegate(typeof(Action),instance,cleanup));
        }
        var warmup=BuildBenchmarkCase(categoryType,vectorType,chunkType,objType,chunks,needed);
        ((Action)Delegate.CreateDelegate(typeof(Action),warmup,cleanup))();
        var watch=Stopwatch.StartNew();
        foreach(var action in actions)action();
        watch.Stop();return watch.ElapsedTicks;
    }

    static object BuildBenchmarkCase(Type categoryType,Type vectorType,Type chunkType,Type objType,int chunks,int needed)
    {
        object category=Activator.CreateInstance(categoryType);
        var neededField=categoryType.GetField("chunksNeeded");
        var removeField=categoryType.GetField("chunksToRemove");
        var dictField=categoryType.GetField("generatedChunks");
        var keys=(System.Collections.IList)Activator.CreateInstance(neededField.FieldType);neededField.SetValue(category,keys);
        removeField.SetValue(category,Activator.CreateInstance(removeField.FieldType));
        var dict=(System.Collections.IDictionary)Activator.CreateInstance(dictField.FieldType);dictField.SetValue(category,dict);
        var vectorCtor=vectorType.GetConstructor(new[]{typeof(double),typeof(double),typeof(double)});
        for(int i=0;i<chunks;i++)
        {
            object key=vectorCtor.Invoke(new object[]{(double)i,0.0,(double)(i%31)});
            if(i<needed)keys.Add(key);
            object chunk=Activator.CreateInstance(chunkType);
            var objs=(System.Collections.IDictionary)chunkType.GetField("objs").GetValue(chunk);
            if(i>=needed)
            {
                object item=Activator.CreateInstance(objType);objType.GetField("place").SetValue(item,true);
                objs.Add(vectorCtor.Invoke(new object[]{(double)(chunks+i),1.0,0.0}),item);
            }
            dict.Add(key,chunk);
        }
        return category;
    }
}

public class objGenScript
{
    public class categoryClass
    {
        public int count;
        public void Cleanup(){throw new Exception("Native cleanup IL was not extracted");}
    }
}
