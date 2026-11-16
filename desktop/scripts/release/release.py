#!/usr/bin/env python3
"""Release metadata, signed manifests and monotonic version detection."""
import argparse, base64, hashlib, json, os, pathlib, re, subprocess, sys, xml.etree.ElementTree as ET
ROOT = pathlib.Path(__file__).resolve().parents[2]
PATTERN = re.compile(r'^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')
def metadata():
    source = (ROOT/'src/HeyRead.Core/ProductInfo.cs').read_text()
    return dict(re.findall(r'public const string (\w+) = "([^"]+)";', source))
def version():
    value = os.environ.get('XIAOHEIHE_BUILD_VERSION') or ET.parse(ROOT/'Version.props').findtext('.//Version')
    if not value or not PATTERN.fullmatch(value): raise ValueError('Version must be numeric X.Y.Z')
    return value
def numeric(value):
    if not PATTERN.fullmatch(value): raise ValueError('Invalid version')
    return tuple(map(int, value.split('.')))
def detect():
    meta = metadata(); current = version()
    releases = json.loads(subprocess.check_output(['gh','release','list','--repo',meta['Repository'],'--limit','100','--json','tagName,isDraft,isPrerelease'],text=True))
    stable = [r['tagName'][len(meta['TagPrefix']):] for r in releases if not r['isDraft'] and not r['isPrerelease'] and r['tagName'].startswith(meta['TagPrefix']) and PATTERN.fullmatch(r['tagName'][len(meta['TagPrefix']):])]
    latest = max(stable,key=numeric) if stable else None
    if latest and numeric(current) < numeric(latest): raise ValueError('Refusing a version downgrade')
    publish = latest is None or numeric(current)>numeric(latest)
    output = f'version={current}\ntag={meta["TagPrefix"]}{current}\npublish={str(publish).lower()}\n'
    if os.environ.get('GITHUB_OUTPUT'):
        with open(os.environ['GITHUB_OUTPUT'],'a') as file:file.write(output)
    print(output,end='')
def manifest(directory, key_file):
    meta=metadata(); v=version(); assets=[]
    for rid in ('osx-arm64','osx-x64'):
        name=f'{meta["PackageName"]}-{v}-{rid}.zip'; path=directory/name
        digest=hashlib.sha256(path.read_bytes()).hexdigest()
        assets.append({'rid':rid,'file':name,'url':f'https://github.com/{meta["Repository"]}/releases/download/{meta["TagPrefix"]}{v}/{name}','size':path.stat().st_size,'sha256':digest})
    doc={'schema':1,'product':meta['PackageName'],'bundleIdentifier':meta['BundleId'],'repository':meta['Repository'],'version':v,'tag':meta['TagPrefix']+v,'assets':assets}
    path=directory/'update-manifest.json';path.write_text(json.dumps(doc,ensure_ascii=False,sort_keys=True,indent=2)+'\n')
    signature=directory/'update-manifest.sig';subprocess.run(['openssl','dgst','-sha256','-sign',str(key_file),'-out',str(signature),str(path)],check=True)
    subprocess.run(['openssl','dgst','-sha256','-verify',str(ROOT/'src/HeyRead.Core/UpdatePublicKey.pem'),'-signature',str(signature),str(path)],check=True)
    sums=[]
    for asset in sorted(directory.iterdir()):
        if asset.suffix in ('.zip','.dmg','.json','.sig'):sums.append(hashlib.sha256(asset.read_bytes()).hexdigest()+'  '+asset.name)
    (directory/'SHA256SUMS.txt').write_text('\n'.join(sums)+'\n')
def main():
    parser=argparse.ArgumentParser();parser.add_argument('command',choices=['version','metadata','detect','manifest']);parser.add_argument('--directory',type=pathlib.Path);parser.add_argument('--key',type=pathlib.Path);args=parser.parse_args()
    if args.command=='version':print(version())
    elif args.command=='metadata':print(json.dumps(metadata(),ensure_ascii=False))
    elif args.command=='detect':detect()
    else:manifest(args.directory,args.key)
if __name__=='__main__':main()
