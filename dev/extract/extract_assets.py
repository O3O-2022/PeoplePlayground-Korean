import UnityPy, json, collections, re
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
import os
G=os.environ.get("PPG_GAME_DIR", r"D:\Steam\steamapps\common\People Playground")  # 게임 폴더
D=G+r"\People Playground_Data"
# pass 1: class + gameobject names (no generator)
env=UnityPy.load(D)
meta={}; errs=collections.Counter()
for obj in env.objects:
    if obj.type.name!="MonoBehaviour": continue
    try:
        mb=obj.read(check_read=False)
        cls=mb.m_Script.read().m_ClassName
        go=""
        try: go=mb.m_GameObject.read().m_Name if mb.m_GameObject and mb.m_GameObject.m_PathID else ""
        except Exception: pass
        meta[(obj.assets_file.name,obj.path_id)]=(cls,go)
    except Exception as e: errs["p1:"+type(e).__name__]+=1
# pass 2: typetrees
env=UnityPy.load(D)
gen=TypeTreeGenerator("2020.3.1f1"); gen.load_local_game(G); env.typetree_generator=gen
out=[]; failcls=collections.Counter()
def walk(v,path,acc):
    if isinstance(v,str):
        if v.strip(): acc.append((path,v))
    elif isinstance(v,dict):
        for k,x in v.items():
            if k in("m_Script","m_GameObject"): continue
            walk(x,f"{path}.{k}" if path else k,acc)
    elif isinstance(v,list):
        for i,x in enumerate(v): walk(x,f"{path}[{i}]",acc)
for obj in env.objects:
    if obj.type.name!="MonoBehaviour": continue
    cls,go=meta.get((obj.assets_file.name,obj.path_id),("?",""))
    try: tt=obj.read_typetree()
    except Exception as e: errs["tt:"+type(e).__name__]+=1; failcls[cls]+=1; continue
    acc=[]; walk(tt,"",acc)
    for p,v in acc: out.append(dict(file=obj.assets_file.name,pid=obj.path_id,cls=cls,go=go,field=p,text=v))
json.dump(out,open("asset_strings.json","w",encoding="utf-8"),ensure_ascii=False,indent=0)
print(len(out),"strings; errors:",errs.most_common(6)); print("failed classes:",failcls.most_common(10))
c=collections.Counter((r['cls'],re.sub(r'\[\d+\]','[]',r['field'])) for r in out)
for k,v in c.most_common(90): print(v,k)
