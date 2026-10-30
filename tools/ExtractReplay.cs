using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Transplants selected production IL into test stand-ins. No game code is checked in.
public static class ExtractReplay
{
    static ModuleDefinition target;
    static Dictionary<string, TypeDefinition> types;
    static MethodDefinition copyDestination;
    static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> list)
    {foreach(var t in list){yield return t;foreach(var n in AllTypes(t.NestedTypes))yield return n;}}
    static void CloneIterator(TypeDefinition source,TypeDefinition parent)
    {
        var t=new TypeDefinition(source.Namespace,source.Name,source.Attributes,MapType(source.BaseType));parent.NestedTypes.Add(t);types.Add(source.FullName,t);
        foreach(var i in source.Interfaces)t.Interfaces.Add(new InterfaceImplementation(MapType(i.InterfaceType)));
        foreach(var f in source.Fields)t.Fields.Add(new FieldDefinition(f.Name,f.Attributes,MapType(f.FieldType)));
        foreach(var m in source.Methods)
        {
            var dest=new MethodDefinition(m.Name,m.Attributes,MapType(m.ReturnType)){ImplAttributes=m.ImplAttributes};
            foreach(var p in m.Parameters)dest.Parameters.Add(new ParameterDefinition(p.Name,p.Attributes,MapType(p.ParameterType)));
            foreach(var o in m.Overrides)dest.Overrides.Add(MapMethod(o));t.Methods.Add(dest);
        }
        foreach(var m in source.Methods)if(m.HasBody)CopyBody(m,t.Methods.Single(x=>x.FullName==m.FullName));
    }
    static TypeReference MapType(TypeReference type)
    {
        if (type is GenericInstanceType)
        {
            var g = (GenericInstanceType)type;
            var result = new GenericInstanceType(MapType(g.ElementType));
            foreach (var a in g.GenericArguments) result.GenericArguments.Add(MapType(a));
            return result;
        }
        if (type is ByReferenceType) return new ByReferenceType(MapType(((ByReferenceType)type).ElementType));
        if (type is ArrayType) return new ArrayType(MapType(((ArrayType)type).ElementType), ((ArrayType)type).Rank);
        if (type is GenericParameter)
        {
            // A transplanted generic method body must reference ITS OWN generic
            // parameters; Cecil cannot emit another module's parameter tokens.
            var parameter = (GenericParameter)type;
            if (copyDestination != null && parameter.Owner is MethodReference && parameter.Position < copyDestination.GenericParameters.Count)
                return copyDestination.GenericParameters[parameter.Position];
            return type;
        }
        TypeDefinition found;
        if (types.TryGetValue(type.FullName, out found))
        {
            // Cecil does not mark an imported enum TypeRef (for example the stock
            // msgType passed through params object[]) as a value type. A replay
            // enum stand-in is still the matching layout.
            if(type.IsValueType!=found.IsValueType && !(found.IsEnum && !type.IsValueType))throw new Exception("Replay value/reference layout mismatch: "+type.FullName);
            return found;
        }
        return target.ImportReference(type);
    }
    static MethodReference MapMethod(MethodReference method)
    {
        if (method is GenericInstanceMethod)
        {
            var g = (GenericInstanceMethod)method;
            var result = new GenericInstanceMethod(MapMethod(g.ElementMethod));
            foreach (var a in g.GenericArguments) result.GenericArguments.Add(MapType(a));
            return result;
        }
        TypeDefinition type;
        if (types.TryGetValue(method.DeclaringType.FullName, out type))
        {
            var found = type.Methods.Where(m => m.Name == method.Name && m.HasThis == method.HasThis && m.Parameters.Count == method.Parameters.Count &&
                m.GenericParameters.Count == method.GenericParameters.Count &&
                m.Parameters.Select(p => TypeKey(p.ParameterType)).SequenceEqual(method.Parameters.Select(p => TypeKey(p.ParameterType)))).ToArray();
            if (found.Length != 1) throw new Exception("Missing replay method: " + method.FullName);
            return found[0];
        }
        if(method.DeclaringType is GenericInstanceType)
        {
            var mapped=new MethodReference(method.Name,MapType(method.ReturnType),MapType(method.DeclaringType)) {HasThis=method.HasThis,ExplicitThis=method.ExplicitThis,CallingConvention=method.CallingConvention};
            foreach(var p in method.Parameters)mapped.Parameters.Add(new ParameterDefinition(MapType(p.ParameterType)));return mapped;
        }
        return target.ImportReference(method);
    }
    static string TypeKey(TypeReference t)
    {
        var p=t as GenericParameter;if(p!=null)return (p.Type==GenericParameterType.Method?"!!":"!")+p.Position;
        var g=t as GenericInstanceType;if(g!=null)return g.ElementType.FullName+"<"+String.Join(",",g.GenericArguments.Select(TypeKey))+">";
        var r=t as ByReferenceType;if(r!=null)return TypeKey(r.ElementType)+"&";
        var a=t as ArrayType;if(a!=null)return TypeKey(a.ElementType)+"["+a.Rank+"]";
        return t.FullName;
    }
    static object MapOperand(object operand, MethodDefinition source, MethodDefinition dest,
        Dictionary<Instruction, Instruction> instructions)
    {
        if (operand == null) return null;
        if (operand is Instruction) return instructions[(Instruction)operand];
        if (operand is Instruction[]) return ((Instruction[])operand).Select(i => instructions[i]).ToArray();
        if (operand is VariableDefinition) return dest.Body.Variables[((VariableDefinition)operand).Index];
        if (operand is ParameterDefinition) return dest.Parameters[((ParameterDefinition)operand).Index];
        if (operand is MethodReference) return MapMethod((MethodReference)operand);
        if (operand is FieldReference)
        {
            var field = (FieldReference)operand;
            TypeDefinition type;
            if (types.TryGetValue(field.DeclaringType.FullName, out type))
            {
                var matches=type.Fields.Where(f=>f.FullName==field.FullName).ToArray();
                if(matches.Length!=1)throw new Exception("Missing or mismatched replay field: "+field.FullName);return matches[0];
            }
            return target.ImportReference(field);
        }
        if (operand is TypeReference) return MapType((TypeReference)operand);
        return operand;
    }
    static void CopyBody(MethodDefinition source, MethodDefinition dest)
    {
        copyDestination = dest;
        dest.Body = new MethodBody(dest) { InitLocals = source.Body.InitLocals, MaxStackSize = source.Body.MaxStackSize };
        foreach (var v in source.Body.Variables) dest.Body.Variables.Add(new VariableDefinition(MapType(v.VariableType)));
        var map = new Dictionary<Instruction, Instruction>();
        foreach (var instruction in source.Body.Instructions)
        {
            var copy = Instruction.Create(OpCodes.Nop);
            copy.OpCode = instruction.OpCode;
            dest.Body.Instructions.Add(copy);
            map.Add(instruction, copy);
        }
        foreach (var instruction in source.Body.Instructions)
            map[instruction].Operand = MapOperand(instruction.Operand, source, dest, map);
        foreach (var h in source.Body.ExceptionHandlers)
        {
            dest.Body.ExceptionHandlers.Add(new ExceptionHandler(h.HandlerType) {
                TryStart = map[h.TryStart], TryEnd = h.TryEnd == null ? null : map[h.TryEnd],
                HandlerStart = map[h.HandlerStart], HandlerEnd = h.HandlerEnd == null ? null : map[h.HandlerEnd],
                FilterStart = h.FilterStart == null ? null : map[h.FilterStart],
                CatchType = h.CatchType == null ? null : MapType(h.CatchType)
            });
        }
        copyDestination = null;
    }
    public static int Main(string[] args)
    {
        try
        {
            if ((args.Length != 3 && args.Length!=4) || File.Exists(args[2]) || args.Length==4 && args[3]!="movement" && args[3]!="driving" && args[3]!="items" && args[3]!="cargo" && args[3]!="seat" && args[3]!="world" && args[3]!="worldnative" && args[3]!="vehicle") throw new Exception("Usage: ExtractReplay SOURCE FIXTURE NEW_OUTPUT [movement|driving|items|cargo|seat|world|worldnative|vehicle]");
            using (var source = AssemblyDefinition.ReadAssembly(args[0]))
            using (var fixture = AssemblyDefinition.ReadAssembly(args[1]))
            {
                target = fixture.MainModule;
                types = AllTypes(target.Types).ToDictionary(t => t.FullName);
                bool movement=args.Length==4 && args[3]=="movement";
                bool driving=args.Length==4 && args[3]=="driving";
                bool items=args.Length==4 && args[3]=="items";
                bool cargo=args.Length==4 && args[3]=="cargo";
                bool seat=args.Length==4 && args[3]=="seat";
                var rosterCore=new[]{"EnsureItemRoster","RegisterRosterItem","RegisterCar","RemoveRosterItem","RemoveIndexed","AddLoose","RemoveLoose","ObserveItemAdded"};
                var requested = args.Length==4 && args[3]=="vehicle"?new Dictionary<string,string[]> {
                    {"TldMp.StableGameBridge",new[]{"ReceiveVehicleMotion","DispatchVehicleVelocity","DispatchSendTime","AllowForward","get_MotionSender"}}
                }:args.Length==4 && args[3]=="worldnative"?new Dictionary<string,string[]> {
                    {"savedatascript",new[]{"SaveToDictionary","LoadStuff","Save","Load","Copy","IndexOfID","IndexOfID2","Remove","Remov2"}},
                    {"itemDataClass",new[]{"Copy","RemoveID"}},
                    {"save_attachable",new[]{"Restart","Save","Load","NeedsSave"}},
                    {"save_physicslock",new[]{"Save","Load","NeedsSave"}}}:args.Length==4 && args[3]=="world"?new Dictionary<string,string[]> {
                    {"TldMp.StableGameBridge",new[]{"CaptureWorld","SendWorld","ApplyWorld"}},
                    {"TldMp.StableJson",new[]{"Write","Read","Serializer"}}}:seat?new Dictionary<string,string[]> {{"TldMp.StableGameBridge",new[]{"SeatKey","PoiSeat","CarKey","CarText","LeaseText","PlayerCar","LocalSeatedCar","MayEnter","EnforceDriverAuthority","DisableWheelSimulation","RestoreWheelSimulation","Capture","Restore","StopReplica","Physics","Render","AttachedToContainer","TransferStoredCargo"}.Concat(rosterCore).ToArray()}}:cargo?new Dictionary<string,string[]> {{"TldMp.StableGameBridge",new[]{"MayFreezeMount","MayStore","MayUnstore","MayMutateCargo","OwnsContainer","RequestCargoAuthority","CargoAuthority","ReassertStored","RejectItemWorldPose","AttachedToContainer"}}}:items?new Dictionary<string,string[]> {{"TldMp.StableGameBridge",new[]{"Publish","PublishUsable","RequestUsable","SendQueuedUsable","CoolUsable","TickPendingUsables","CancelPendingUsable","ReconcileUsableGrant","CacheUsable","RestoreUsable","ResyncUsable","FlushDeferred","ReceiveItemPose","PlainItem","CanBuffer","StorageLocked","LocalStoragePose","RejectItemWorldPose","ReassertStored","StopReplica","Capture","Restore","LiveHandoff","Body","Find","V","G","get_Now","Render","SafeVelocity","SafeAngularVelocity","AddLoose","RemoveLoose","RemoveIndexed","SendItem","RequestOwnerRepair","ValidateOwnerRepair","HandleOwnerRepair","MayDecideCarFalloff","CanGrantPushLease","ReplicaEligible"}}}:driving?new Dictionary<string,string[]> {{"tosaveitemscript",new[]{"ThisOneInCharge","ThisOneInChargeCar","CarWhatDoing","OtherInChargeItem"}}}:movement?new Dictionary<string,string[]> {{"syncScript",new[]{"ItemPosLocal","UpdAttachable","SmoothMove"}}}:new Dictionary<string, string[]> {
                    { "jointHelpScript", new[] { "UnFreezeStart" } },
                    { "tosaveitemscript", new[] { "GotSync", "UpdGotSyncTime" } },
                    { "GeneszSteamP2PBase.SteamP2PNetworkingUnderTheHoodScript", new[] { "Init", "ReadMessage" } }
                };
                if(cargo)requested["TldMp.StableGameBridge"]=requested["TldMp.StableGameBridge"].Concat(new[]{"TransferStoredCargo"}).ToArray();
                if(movement)
                {
                    var st=source.MainModule.Types.Single(t=>t.FullName=="syncScript");
                    foreach(var t in st.NestedTypes.Where(t=>requested["syncScript"].Any(n=>t.Name.StartsWith("<"+n+">",StringComparison.Ordinal))))CloneIterator(t,types["syncScript"]);
                }
                foreach (var entry in requested)
                {
                    var st = source.MainModule.Types.Single(t => t.FullName == entry.Key);
                    var dt = types[entry.Key];
                    foreach (var sm in st.Methods.Where(m => m.Name.StartsWith("TldMp_")))
                    {
                        var dm = new MethodDefinition(sm.Name, sm.Attributes, MapType(sm.ReturnType));
                        foreach (var p in sm.Parameters) dm.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, MapType(p.ParameterType)));
                        dt.Methods.Add(dm);
                    }
                }
                foreach (var entry in requested)
                {
                    var st = source.MainModule.Types.Single(t => t.FullName == entry.Key);
                    var dt = types[entry.Key];
                    foreach (var sm in st.Methods.Where(m => entry.Value.Contains(m.Name) || m.Name.StartsWith("TldMp_")))
                    {
                        CopyBody(sm, dt.Methods.Single(m => m.FullName == sm.FullName));
                        Console.WriteLine("Extracted production IL: " + sm.FullName);
                    }
                }
                if (target.AssemblyReferences.Any(r => r.Name == "Assembly-CSharp" || r.Name == "Tld.Multiplayer" || r.Name.StartsWith("UnityEngine")))
                    throw new Exception("Replay still references the game, runtime, or Unity; isolation failed: "+String.Join(",",target.AssemblyReferences.Where(r=>r.Name=="Assembly-CSharp"||r.Name=="Tld.Multiplayer"||r.Name.StartsWith("UnityEngine")).Select(r=>r.Name).ToArray()));
                fixture.Write(args[2]);
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
