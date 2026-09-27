import json,re,collections
A=json.load(open("asset_strings.json",encoding="utf-8"))
C=json.load(open("code_strings.json",encoding="utf-8"))
VIS={('SpawnableAsset','m_Name'),('SpawnableAsset','Description'),('TextMeshProUGUI','m_text'),('TextMeshPro','m_text'),('Text','m_Text'),
 ('HasTooltipBehaviour','Text'),('ToolLibrary','Tools[].Name'),('ToolLibrary','Tools[].Description'),('ToolLibrary','Powers[].Name'),('ToolLibrary','Powers[].Description'),
 ('Map','m_Name'),('Map','Description'),('PageSwitchButtonBehaviour','pageName'),('UIStatViewBehaviour','StatName'),('Category','m_Name'),('Category','Description'),
 ('ConditionalTextBehaviour','TrueText'),('ConditionalTextBehaviour','FalseText'),('SteamWarningDialog','Title'),('SteamWarningDialog','Message'),
 ('ModListBehaviour','FallbackModDescription'),('ShowDialogBehaviour','Title'),('ShowDialogBehaviour','Message'),('MapEditorAsset','Description'),
 ('HeartMonitorBehaviour','NoWireAttachedText'),('AirfoilBehaviour','WingTypes[].Name')}
items=collections.OrderedDict()   # en -> info
def add(en,kind,src):
    if not en.strip() or not re.search(r'[A-Za-z]{2}',en): return
    d=items.setdefault(en,{"kinds":set(),"src":[]}); d["kinds"].add(kind); 
    if len(d["src"])<3: d["src"].append(src)
for r in A:
    f=re.sub(r'\[\d+\]','[]',r['field'])
    if (r['cls'],f) in VIS:
        kind="item_name" if (r['cls'],f)==('SpawnableAsset','m_Name') else "item_desc" if (r['cls'],f)==('SpawnableAsset','Description') else "asset"
        add(r['text'],kind,f"{r['cls']}.{f} @{r['go']}")
SKIPUSE=re.compile(r'ShaderProperties::Get|Resources::Load|InputSystem::|Debug::Log|Liquid::GetLiquid|PlayerPrefs::|Transform::Find|LayerMask::|SendMessage|BroadcastMessage|Exception::\.ctor|set_sortingLayerName|Path::|Directory::|CompareTag|ShaderLibrary|StatCollection|Stat::\.ctor|UnlockAchievement|NameToLayer|Coroutine|op_Equality|op_Inequality|GetType|Animator|Shader::|Material::|Set(Float|Color|Int|Vector|Texture)|GetComponent|FBSF|File::|Contains|StartsWith|EndsWith|Replace|IndexOf|Split|GameObject::\.ctor|Object::get_name|Find|Invoke|PlayerPrefs|Guid|Regex|Encoding|Load|Instantiate|Tag')
LOGISH=re.compile(r'(?i)does not exist|is null|out of range|exception|null signal|not allowed|should never|stack ?trace|\bid\b.*\{|deserial|serialis|pool$|\bNULL\b|renderer for|tried to|could not find|invalid|missing|not found|obsolete|^\s|\s$')
frag=[];fmt=[]
for r in C:
    s=r['text']; u=r['use']
    if SKIPUSE.search(u): continue
    if re.fullmatch(r'[\w.\-/\:]+',s) and not re.fullmatch(r'[A-Z][a-z]+( [a-z]+)*|[A-Z]+',s): continue   # ids/paths but keep 'Break','ACID'
    if re.fullmatch(r'[a-z]+[A-Z]\w*|[A-Z][a-z]+[A-Z]\w*',s): continue  # camelCase
    if not re.search(r'[A-Za-z]{2}',s) or s.startswith(('http','{"','_','#','aHR0')) or re.search(r'\.(png|json|txt|dll|md|jpg|wav)\b',s): continue
    src=f"{r['type']}.{r['method']}|{u}"
    if 'String::Concat' in u or 'StringBuilder' in u or s!=s.strip():
        frag.append((s,src)); continue
    if LOGISH.search(s): continue
    if re.search(r'\{\d',s): fmt.append((s,src)); add(s,"format",src); continue
    add(s,"code",src)
out=[{"en":k,"kinds":sorted(v["kinds"]),"src":v["src"]} for k,v in items.items()]
json.dump(out,open("tr_list.json","w",encoding="utf-8"),ensure_ascii=False,indent=0)
json.dump(frag,open("fragments.json","w",encoding="utf-8"),ensure_ascii=False,indent=0)
kc=collections.Counter(k for o in out for k in o['kinds'][:1])
print(len(out),"entries",kc,"| fragments",len(frag),"| formats",len(fmt), "| chars",sum(len(o['en']) for o in out))
