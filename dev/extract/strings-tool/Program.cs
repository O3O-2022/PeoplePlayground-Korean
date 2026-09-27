// 게임 코드(Assembly-CSharp.dll)에서 번역 대상 문자열을 뽑는다.
//  1) 코드 안의 문자열 리터럴(ldstr)과 그 문자열이 어디에 쓰이는지(다음 호출) → code_strings.json
//  2) 속성(attribute)의 문자열 인수 (설정 이름/설명 등) → attr_strings.json
// 사용법: dotnet run -c Release -- "<게임폴더>" "<출력폴더>"
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Text.Json;

string game = args[0], outDir = args[1];
string managed = Path.Combine(game, "People Playground_Data", "Managed");
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(managed);
var asm = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "Assembly-CSharp.dll"), new ReaderParameters { AssemblyResolver = resolver });
var json = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

var code = new List<object>();
foreach (var t in asm.MainModule.GetTypes())
foreach (var m in t.Methods)
{
    if (!m.HasBody) continue;
    var ins = m.Body.Instructions;
    for (int i = 0; i < ins.Count; i++)
    {
        if (ins[i].OpCode != OpCodes.Ldstr) continue;
        string use = "";
        for (int j = i + 1; j < Math.Min(ins.Count, i + 14); j++)
        {
            var op = ins[j].OpCode;
            if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj || op == OpCodes.Stfld || op == OpCodes.Stsfld)
            {
                var mr = ins[j].Operand as MemberReference;
                use = op.Name + " " + (mr?.DeclaringType?.Name ?? "") + "::" + (mr?.Name ?? "");
                break;
            }
        }
        code.Add(new { type = t.FullName, method = m.Name, text = (string)ins[i].Operand, use });
    }
}

var attrs = new List<object>();
void Scan(ICustomAttributeProvider p, string where)
{
    if (!p.HasCustomAttributes) return;
    foreach (var ca in p.CustomAttributes)
    {
        var a = new List<string>();
        foreach (var arg in ca.ConstructorArguments) if (arg.Value is string s) a.Add(s);
        foreach (var arg in ca.Properties) if (arg.Argument.Value is string s2) a.Add(arg.Name + "=" + s2);
        if (a.Count > 0) attrs.Add(new { attr = ca.AttributeType.Name, where, args = a });
    }
}
foreach (var t in asm.MainModule.GetTypes())
{
    Scan(t, t.FullName);
    foreach (var f in t.Fields) Scan(f, t.FullName + "." + f.Name);
    foreach (var pr in t.Properties) Scan(pr, t.FullName + "." + pr.Name);
}

Directory.CreateDirectory(outDir);
File.WriteAllText(Path.Combine(outDir, "code_strings.json"), JsonSerializer.Serialize(code, json));
File.WriteAllText(Path.Combine(outDir, "attr_strings.json"), JsonSerializer.Serialize(attrs, json));
Console.WriteLine($"code strings: {code.Count}, attribute strings: {attrs.Count}");
