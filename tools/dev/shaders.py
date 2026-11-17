import sys, UnityPy
env = UnityPy.load(sys.argv[1]); n=0
for obj in env.objects:
    if obj.type.name == "Shader":
        d = obj.read(); s = getattr(d, "m_Script", "") or ""
        if isinstance(s, bytes): s = s.decode("utf-8","replace")
        n+=1
        if n<=3: print("----", d.m_Name, len(s)); print(s[:600])
        else: print(d.m_Name, len(s), "Program" in s, "CGPROGRAM" in s)
