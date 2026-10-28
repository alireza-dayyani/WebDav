"""Opt-in local demo data. Credentials and results stay in the ignored private data folder."""
import argparse
import contextlib
import datetime
import getpass
import http.cookiejar
import json
import os
from pathlib import Path
import secrets
import socket
import struct
import subprocess
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
import zlib

ROOT = Path(__file__).resolve().parents[1]
PEOPLE = [('ali', 'علی نمونه', 'Ali Sample'), ('sara', 'سارا نمونه', 'Sara Sample'),
          ('neda', 'ندا نمونه', 'Neda Sample'), ('omid', 'امید نمونه', 'Omid Sample'),
          ('maryam', 'مریم نمونه', 'Maryam Sample'), ('reza', 'رضا نمونه', 'Reza Sample'),
          ('laleh', 'لاله نمونه', 'Laleh Sample'), ('john', 'جان نمونه', 'John Sample')]


class Api:
    def __init__(self, base):
        self.base = base.rstrip('/')
        self.opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))

    def request(self, path, method='GET', data=None, mime='application/json', binary=False):
        headers = {'Content-Type': mime}
        if method != 'GET':
            headers['X-CSRF-TOKEN'] = self.request('/api/csrf')['token']
        try:
            with self.opener.open(urllib.request.Request(self.base + path, data=data, method=method, headers=headers), timeout=240) as response:
                value = response.read()
        except urllib.error.HTTPError as error:
            # Do not print request payloads or credentials on failure.
            raise RuntimeError(f'{method} {path}: HTTP {error.code}') from None
        return value if binary else json.loads(value) if value else None

    def json(self, path, method='GET', value=None):
        data = json.dumps(value, ensure_ascii=False).encode() if value is not None else None
        return self.request('/api' + path, method, data)

    def upload(self, path, name, content, title):
        boundary = 'inkstone-demo-' + secrets.token_hex(10)
        data = (f'--{boundary}\r\nContent-Disposition: form-data; name="title"\r\n\r\n{title}\r\n'
                f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{name}"\r\nContent-Type: application/octet-stream\r\n\r\n').encode()
        data += content + f'\r\n--{boundary}--\r\n'.encode()
        return self.request('/api' + path, 'POST', data, 'multipart/form-data; boundary=' + boundary)

    def all(self, path):
        page, rows = 1, []
        while True:
            result = self.json(path + '?page=' + str(page)); rows.extend(result['items'])
            if not result['hasMore']: return rows
            page += 1


def ref(item):
    metadata = item.get('metadata', item)
    return {'id': metadata['id'], 'version': metadata['version']}


def demo_png(variant=0, logo=False):
    """Original geometric logo or clearly synthetic pen strokes; no real signature or font."""
    width, height = (160, 160) if logo else (400, 120)
    pixels = bytearray([255] * width * height * 3)
    def dot(x, y, color, radius=2):
        for yy in range(max(0,y-radius), min(height,y+radius+1)):
            for xx in range(max(0,x-radius), min(width,x+radius+1)):
                at = (yy*width+xx)*3; pixels[at:at+3] = bytes(color)
    def line(a, b, color, radius=2):
        steps = max(abs(b[0]-a[0]), abs(b[1]-a[1]), 1)
        for i in range(steps+1): dot(round(a[0]+(b[0]-a[0])*i/steps), round(a[1]+(b[1]-a[1])*i/steps), color, radius)
    if logo:
        for y in range(8,152):
            for x in range(8,152): dot(x,y,(21,94,80),0)
        for a,b in [((40,115),(80,35)),((80,35),(120,115)),((53,90),(107,90))]: line(a,b,(255,255,255),4)
    else:
        shift = variant * 4
        points = [(25,88),(50,35+shift),(68,87),(93,26),(80,93),(120,61),(109,87),(146,64),(159,80),(184,50+shift),(179,89),(220,60),(233,78),(266,53),(254,85),(309,58),(345,75)]
        for a,b in zip(points, points[1:]): line(a,b,(30,56,90),2)
        line((30,100),(355,93),(30,56,90),1)
        # A small original block-letter DEMO watermark makes the synthetic status visible.
        glyphs = ['110101101101110','111100110100111','101111111101101','111101101101111']
        for i,glyph in enumerate(glyphs):
            for n,bit in enumerate(glyph):
                if bit == '1': dot(302+i*15+(n%3)*3,12+(n//3)*3,(135,145,155),1)
    raw = b''.join(b'\0' + bytes(pixels[y*width*3:(y+1)*width*3]) for y in range(height))
    def chunk(kind, value): return struct.pack('!I',len(value)) + kind + value + struct.pack('!I',zlib.crc32(kind+value)&0xffffffff)
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR',struct.pack('!2I5B',width,height,8,2,0,0,0)) + chunk(b'IDAT',zlib.compress(raw)) + chunk(b'IEND',b'')


def seed_resources(api, person, recipients, extras):
    templates, senders, images = api.all('/templates/'), api.all('/letters/senders/'), api.all('/letters/images/')
    def ensure(rows, title, create):
        found = next((x for x in rows if x['metadata']['title'] == title), None)
        if found: return found
        item = create(); rows.append(item); return item
    fa = ensure(templates, '[Demo] نامه رسمی فارسی A5', lambda: api.json('/templates/starter','POST',{'title':'[Demo] نامه رسمی فارسی A5','kind':'letter','language':'fa-IR','paper':'A5'}))
    fa4 = ensure(templates, '[Demo] نامه رسمی فارسی A4', lambda: api.json('/templates/starter','POST',{'title':'[Demo] نامه رسمی فارسی A4','kind':'letter','language':'fa-IR','paper':'A4'}))
    en = ensure(templates, '[Demo] English block letter', lambda: api.json('/templates/starter','POST',{'title':'[Demo] English block letter','kind':'letter','language':'en-US','paper':'Letter'}))
    sender = ensure(senders,'مدیر پروژه (نمونه)',lambda: api.json('/letters/senders/','POST',{'version':0,'fullName':person[1],'title':'مدیر پروژه (نمونه)','organization':'سازمان آزمایشی اینکستون','address':'نشانی آزمایشی — بدون مقصد واقعی'}))
    ensure(senders,'Project lead (demo)',lambda: api.json('/letters/senders/','POST',{'version':0,'fullName':person[2],'title':'Project lead (demo)','organization':'Inkstone demonstration team','address':'Example address, no delivery'}))
    signature = ensure(images,'[Demo] Synthetic signature 1',lambda: api.upload('/letters/images/','signature-1.png',demo_png(),'[Demo] Synthetic signature 1'))
    ensure(images,'[Demo] Synthetic signature 2',lambda: api.upload('/letters/images/','signature-2.png',demo_png(1),'[Demo] Synthetic signature 2'))
    logo = ensure(images,'[Demo] Original sample logo',lambda: api.upload('/letters/images/','logo.png',demo_png(logo=True),'[Demo] Original sample logo'))
    attachments = []
    if extras:
        # A distinct reply template, made from a private original source with an extra heading.
        if not any(t['metadata']['title'] == '[Demo] پاسخ رسمی فارسی A4' for t in templates):
            model = json.loads(api.request(f'/api/templates/{fa4["metadata"]["id"]}/download?version={fa4["metadata"]["version"]}&format=json',binary=True))
            heading = 'پاسخ به مکاتبهٔ پیشین'
            model['body'].insert(0,{'kind':'para','text':heading,'runs':[{'len':len(heading.encode()),'props':{'bold':True}}],'props':{'bidi':True}})
            source = api.upload('/documents/upload','reply-template.json',json.dumps(model,ensure_ascii=False).encode(),'[Demo] Reply template source')
            template = api.json('/templates/','POST',{'title':'[Demo] پاسخ رسمی فارسی A4','kind':'letter','language':'fa-IR','source':ref(source),'fields':fa4['data']['fields'],'requiredFields':fa4['data']['requiredFields']})
            templates.append(template)
        ensure(templates,'[Demo] Meeting notes',lambda: api.json('/templates/starter','POST',{'title':'[Demo] Meeting notes','kind':'document','language':'en-US'}))
        documents = api.json('/documents/')
        for title, text in [('[Demo] Project outline','Original sample project outline.\nMilestones: draft, review, approval.'),('[Demo] پیوست فارسی','این پیوست صرفاً دادهٔ آزمایشی برای بررسی ساخت نامه است.')]:
            document = next((d for d in documents if d['title'] == title), None)
            if document is None: document = api.upload('/documents/upload','attachment.txt',text.encode(),title)
            attachments.append(ref(document))
    title = '[Demo] نامه آماده برای ویرایش'
    letters = api.all('/letters/')
    letter = next((x for x in letters if x['metadata']['title'] == title), None)
    if letter is None:
        draft = {'title':title,'subject':'بررسی نمونهٔ گردش مکاتبات','language':'fa-IR','date':datetime.date.today().isoformat(),
                 'template':ref(fa4),'sender':ref(sender),'signature':ref(signature),'logo':ref(logo),'bodyDocument':None,
                 'bodyText':'با سلام،\nاین متن آزمایشی را در ویرایشگر قالب باز کنید و متن نامهٔ خود را بنویسید. گیرنده، نام و سمت فرستنده و تصویر امضا از مشخصات انتخاب‌شده تکمیل می‌شوند.\nبا سپاس',
                 'recipients':[{'userId':recipients[0],'kind':'to','printOnLetter':True},{'userId':recipients[1],'kind':'cc','printOnLetter':True}],
                 'attachments':attachments,'number':'','priority':'normal','classification':'normal','includeQrCode':True,'copyVariants':True}
        letter = api.json('/letters/','POST',draft)
        letter = api.json(f'/letters/{letter["metadata"]["id"]}/editor','POST',{'version':1})
    formatted_title = '[Demo] نامه با قالب رسمی جدید'
    formatted = next((x for x in letters if x['metadata']['title'] == formatted_title), None)
    if formatted is None:
        formatted = api.json('/letters/','POST',{'title':formatted_title,'subject':'درخواست بررسی برنامهٔ پیشنهادی','language':'fa-IR','date':datetime.date.today().isoformat(),
                 'template':ref(fa),'sender':ref(sender),'signature':ref(signature),'logo':ref(logo),'bodyDocument':None,
                 'bodyText':'خواهشمند است برنامهٔ پیشنهادی را بررسی فرمایید و نتیجه را برای هماهنگی مراحل بعدی اعلام کنید.',
                 'recipients':[{'userId':recipients[0],'kind':'to','printOnLetter':True}],
                 'attachments':[],'includeQrCode':True,'copyVariants':False})
        formatted = api.json(f'/letters/{formatted["metadata"]["id"]}/editor','POST',{'version':1})
    if extras and not any(x['metadata']['title'] == '[Demo] Finalized sample letter' for x in letters):
        duplicate = api.json(f'/letters/{letter["metadata"]["id"]}/duplicate','POST',{'version':letter['metadata']['version'],'title':'[Demo] Finalized sample letter'})
        api.json(f'/letters/{duplicate["metadata"]["id"]}/generate','POST',{'version':1,'idempotencyKey':str(uuid.uuid4())})
    return {'templates':[x['metadata']['id'] for x in templates], 'editableLetterId':letter['metadata']['id'], 'formattedLetterId':formatted['metadata']['id']}


def protect(directory):
    directory.mkdir(parents=True,exist_ok=True)
    if os.name == 'nt':
        principal = os.environ.get('USERDOMAIN','') + '\\' + getpass.getuser()
        subprocess.run(['icacls',str(directory),'/inheritance:r','/grant:r',principal+':(OI)(CI)F','*S-1-5-18:(OI)(CI)F','*S-1-5-32-544:(OI)(CI)F'],check=True,stdout=subprocess.DEVNULL)
    else: directory.chmod(0o700)


def save_manifest(path, manifest):
    temporary = path.with_suffix('.tmp')
    temporary.write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
    if os.name != 'nt': temporary.chmod(0o600)
    temporary.replace(path)


@contextlib.contextmanager
def local_server(data):
    with socket.socket() as sock:
        sock.bind(('127.0.0.1',0)); port = sock.getsockname()[1]
    base = 'http://127.0.0.1:' + str(port)
    environment = {**os.environ,'ASPNETCORE_ENVIRONMENT':'Development','Storage__DataDirectory':str(data)}
    with open(data/'demo/seed.log','w',encoding='utf-8') as log:
        process = subprocess.Popen(['dotnet',str(ROOT/'Back/bin/Debug/net10.0/Inkstone.Api.dll'),'--urls',base],cwd=ROOT/'Back',env=environment,stdout=log,stderr=log)
        try:
            for _ in range(120):
                if process.poll() is not None: raise RuntimeError('Demo API failed to start; see the private seed log.')
                try:
                    Api(base).json('/health'); break
                except OSError: time.sleep(.25)
            else: raise RuntimeError('Demo API startup timed out.')
            yield base
        finally:
            process.terminate(); process.wait(timeout=20)


def seed(base, data):
    private = data/'demo'; protect(private)
    path = private/'accounts.json'
    manifest = json.loads(path.read_text(encoding='utf-8')) if path.exists() else {'accounts':[]}
    clients = []
    for person in PEOPLE:
        email = 'demo.'+person[0]+'@example.test'
        account = next((x for x in manifest['accounts'] if x['email'] == email), None)
        api = Api(base)
        if account is None:
            account = {'email':email,'password':secrets.token_urlsafe(24)+'aA1!'}
            api.json('/account/register','POST',{'email':email,'password':account['password']})
            manifest['accounts'].append(account)
            save_manifest(path,manifest)
        else: api.json('/account/login','POST',{'email':email,'password':account['password']})
        account['id'] = next(user['id'] for user in api.json('/letters/directory?search='+urllib.parse.quote(email,safe=''))['items'] if user['email'] == email)
        clients.append((api,person,account))
        save_manifest(path,manifest)
    for i,(api,person,account) in enumerate(clients):
        others = [clients[(i+1)%len(clients)][2]['id'],clients[(i+2)%len(clients)][2]['id']]
        account['resources'] = seed_resources(api,person,others,i == 0)
        save_manifest(path,manifest)
    bootstrap = Path(os.environ.get('BootstrapAdmin__File',str(data/'bootstrap/superadmin.json')))
    if bootstrap.exists():
        config = {key.lower():value for key,value in json.loads(bootstrap.read_text(encoding='utf-8-sig')).items()}
        api = Api(base)
        try: api.json('/account/login','POST',{'email':config['email'],'password':config['password']})
        except RuntimeError: print('Bootstrap login unavailable; demo accounts were prepared independently.')
        else: manifest['bootstrapResources'] = seed_resources(api,('admin','مدیر سامانهٔ نمونه','Sample administrator'),[clients[0][2]['id'],clients[1][2]['id']],True)
    save_manifest(path,manifest)
    if os.name != 'nt': path.chmod(0o600)
    print(f'Prepared {len(clients)} demo users with private templates, sender titles, synthetic signatures, logos and editable letters.')
    print('Private credentials and resource IDs: ' + str(path))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--api-url',help='Use an already-running loopback Inkstone API instead of a temporary local process.')
    parser.add_argument('--data-directory',type=Path,default=Path(os.environ.get('Storage__DataDirectory',str(ROOT/'Back/App_Data'))))
    args = parser.parse_args()
    data = args.data_directory.resolve(); protect(data/'demo')
    if args.api_url:
        parsed = urllib.parse.urlparse(args.api_url)
        if parsed.scheme not in ('http','https') or parsed.hostname not in ('127.0.0.1','localhost','::1') or parsed.username or parsed.password: parser.error('Demo seeding is restricted to loopback HTTP(S) hosts.')
        seed(args.api_url,data)
    else:
        with local_server(data) as base: seed(base,data)
