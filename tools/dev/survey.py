import sys, collections, UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler
env = UnityPy.load(sys.argv[1])
tf = collections.Counter(); af = collections.Counter(); n=0
for obj in env.objects:
    t = obj.type.name
    if t == "Texture2D":
        d = obj.read(); tf[str(d.m_TextureFormat)] += 1
    elif t == "AudioClip":
        d = obj.read(); af[(getattr(d,"m_Type",None), getattr(d,"m_Format",None), bytes(d.m_AudioData[:4]) if d.m_AudioData else None)] += 1
    elif t == "Mesh" and n < 2:
        d = obj.read(); n += 1
        h = MeshHandler(d); h.process()
        print("mesh", d.m_Name, len(h.m_Vertices or []), "uv0", len(h.m_UV0 or []), "sub", len(d.m_SubMeshes), "bones", len(d.m_BindPose), "idx", len(h.m_IndexBuffer or []))
print(tf); print(af)
for obj in env.objects:
    if obj.type.name == "ResourceManager":
        tt = obj.read_typetree(); c = tt["m_Container"]; print("resources", len(c), c[:3]); break
for obj in env.objects:
    if obj.type.name == "BuildSettings":
        print(obj.read_typetree()); break
