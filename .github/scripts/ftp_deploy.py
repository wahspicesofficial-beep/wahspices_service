"""Deploy a publish folder to MonsterASP wwwroot over FTPS: app_offline -> clean -> upload -> remove app_offline.

The server keeps its OWN appsettings*.json (real values, managed by the owner): this script never uploads,
overwrites or deletes them. Usage: python3 -I ftp_deploy.py <netrc file> <publish folder>
"""
import fnmatch, ftplib, io, netrc, os, sys, time
KEEP_ON_SERVER = ('appsettings*.json',)
def keep(name): return any(fnmatch.fnmatch(name.lower(), p) for p in KEEP_ON_SERVER)
host='site110448.siteasp.net'; ROOT='wwwroot'
login,_,pw = netrc.netrc(sys.argv[1]).authenticators(host); SRC=sys.argv[2]
def connect():
    f=ftplib.FTP_TLS(host, timeout=120); f.login(login,pw); f.prot_p(); return f
f=connect()
log=lambda *a: print(time.strftime('%H:%M:%S'), *a, flush=True)

f.storbinary(f'STOR {ROOT}/app_offline.htm', io.BytesIO(b'<html><body style="font-family:sans-serif;padding:40px">WAH! Spices API is being updated. Back in a minute.</body></html>'))
log('app_offline.htm uploaded'); time.sleep(5)

def rmtree(path):
    for name, fa in list(f.mlsd(path)):
        if name in ('.','..'): continue
        p=f'{path}/{name}'
        if fa.get('type')=='dir': rmtree(p); f.rmd(p)
        else: f.delete(p)
removed=0
for name, fa in list(f.mlsd(ROOT)):
    if name in ('.','..','app_offline.htm') or keep(name): continue
    p=f'{ROOT}/{name}'
    if fa.get('type')=='dir': rmtree(p); f.rmd(p)
    else: f.delete(p)
    removed+=1
log(f'old content removed ({removed} top-level items)')

files=[]; dirs=set()
for d,_,fs in os.walk(SRC):
    rel=os.path.relpath(d,SRC); 
    if rel!='.': dirs.add(rel)
    files+= [os.path.join(rel,x) if rel!='.' else x for x in fs if x!='.DS_Store' and not (rel=='.' and keep(x))]
for d in sorted(dirs, key=lambda x: x.count(os.sep)):
    try: f.mkd(f'{ROOT}/{d}')
    except ftplib.error_perm: pass
# web.config last, so the app only starts once every DLL is in place
files.sort(key=lambda x: x=='web.config')
total=sum(os.path.getsize(os.path.join(SRC,x)) for x in files); done=0
for i,rel in enumerate(files,1):
    for attempt in range(3):
        try:
            with open(os.path.join(SRC,rel),'rb') as fh: f.storbinary(f'STOR {ROOT}/{rel}', fh, 262144)
            break
        except (ftplib.all_errors) as e:
            log('retry', rel, e); time.sleep(3)
            try: f.quit()
            except Exception: pass
            f=connect()
    else: sys.exit(f'FAILED to upload {rel}')
    done+=os.path.getsize(os.path.join(SRC,rel))
    if i%40==0 or i==len(files): log(f'uploaded {i}/{len(files)} files, {done*100//total}%')
f.delete(f'{ROOT}/app_offline.htm'); log('app_offline.htm removed - DONE')
f.quit()
