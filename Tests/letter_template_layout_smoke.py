"""Reference-based typography, geometry, bookmark safety and real DOCX/PDF exports."""
import importlib.util
import json
import uuid
import io
import zipfile
import xml.etree.ElementTree as ET
from studio_test_support import Client, ROOT, reference, server, docx_text

spec = importlib.util.spec_from_file_location('demo_art',ROOT/'Scripts/seed_letter_demo.py')
art = importlib.util.module_from_spec(spec); spec.loader.exec_module(art)
NS = {'w':'http://schemas.openxmlformats.org/wordprocessingml/2006/main'}
with server(5088,'letter-layout') as (base,_):
    author, other = Client(base), Client(base)
    author.register('layout-author'); recipient = other.register('layout-recipient')
    signature = author.multipart('/letters/images/','signature.png',art.demo_png(),{'title':'Original demonstration signature'})
    logo = author.multipart('/letters/images/','logo.png',art.demo_png(logo=True),{'title':'Original geometric logo'})
    for language,paper,filename in [('fa-IR','A5','persian-official-a5'),('fa-IR','A4','persian-official-a4'),('en-US','Letter','english-business-letter')]:
        persian = language == 'fa-IR'
        request = {'title':filename,'kind':'letter','language':language}
        if paper == 'A4': request['paper'] = paper
        template = author.json('/templates/starter','POST',request,expected=201)
        route = f'/api/templates/{template["metadata"]["id"]}/download?version=1&format=json'
        model = json.loads(author.raw(route)[0])
        section = model['lastSection']
        expected = {'A5':(419.5276,595.2756),'A4':(595.2756,841.8898),'Letter':(612,792)}[paper]
        assert abs(section['pageW']-expected[0]) < .05 and abs(section['pageH']-expected[1]) < .05
        styles = {s['id']:s for s in model['styles']['styles']}
        body = styles['InkstoneLetterBody']; heading = styles['InkstoneLetterHeading']
        assert body['chr']['font'] == ('B Zar' if persian else 'Times New Roman')
        assert body['chr']['fontCs'] == body['chr']['font'] and body['chr']['sizeCs'] == (13 if persian else 12)
        assert body['chr']['boldCs'] == persian and body['para']['bidi'] == persian
        assert heading['chr']['font'] == ('B Titr' if persian else 'Times New Roman')
        assert heading['chr']['size'] == (11 if persian else 12)
        if persian:
            assert styles['InkstoneLetterCopies']['chr']['size'] == 9
            assert styles['InkstoneLetterContact']['chr']['size'] == 7
        else:
            assert section['marginLeft'] == section['marginRight'] == 72
            assert body['para']['spaceAfter'] == 12 and body['para']['lineSpacing'] == {'rule':'multiple','value':1.0}
        bookmarks = author.json(f'/templates/{template["metadata"]["id"]}/bookmarks?version=1')['bookmarks']
        assert len(bookmarks) == 17 and all(b['replaceable'] for b in bookmarks)
        profile = author.json('/letters/senders/','POST',{'version':0,'fullName':'علی نمونه' if persian else 'Alex Morgan',
            'title':'مدیر برنامه‌ریزی' if persian else 'Operations Director','organization':'شرکت توسعهٔ همکاری' if persian else 'Northbridge Consulting',
            'address':'تهران، خیابان نمونه، پلاک ۱۲ | contact@example.test' if persian else '125 Example Street\nBoston, MA 02110'},expected=201)
        text = ('خواهشمند است برنامهٔ پیشنهادی همکاری را بررسی فرمایید.\nنتیجهٔ بررسی را برای هماهنگی جلسهٔ آینده اعلام کنید.' if persian else
                'Thank you for reviewing our proposed service schedule. We would like to confirm the next steps for the coming quarter.\nPlease review the attached outline and share any changes by Friday. We will incorporate your feedback before the planning meeting.\nWe appreciate your time and look forward to working together.')
        if paper == 'A4':
            text += '\n' + '\n'.join(f'بند {index}: این متنِ نمونه برای بررسی صفحه‌بندی نامهٔ بلند تهیه شده است. خواهشمند است جزئیات برنامه را بررسی و پیشنهادهای خود را اعلام فرمایید.' for index in range(1,21))
        draft = {'title':filename,'subject':'درخواست بررسی برنامهٔ همکاری' if persian else 'Proposed service schedule', 'language':language,'date':'2026-10-27',
            'template':reference(template),'sender':reference(profile),'signature':reference(signature),'logo':reference(logo),'bodyDocument':None,'bodyText':text,
            'recipients':[{'userId':recipient['id'],'kind':'to','printOnLetter':True}], 'attachments':[], 'includeQrCode':True}
        letter = author.json('/letters/','POST',draft,expected=201)
        path = f'/letters/{letter["metadata"]["id"]}'
        result = author.json(path+'/generate','POST',{'version':1,'idempotencyKey':str(uuid.uuid4())})
        artifact = result['artifacts'][0]
        download = f'/api{path}/artifacts/{artifact["id"]}/download?version=2'
        docx = author.raw(download)[0]; pdf = author.raw(download+'&format=pdf')[0]
        assert pdf.startswith(b'%PDF-') and text.split('\n')[0] in docx_text(docx)
        if not persian: assert 'October 27, 2026' in docx_text(docx) and 'Sincerely,' in docx_text(docx)
        with zipfile.ZipFile(io.BytesIO(docx)) as package:
            document = ET.fromstring(package.read('word/document.xml'))
            paragraphs = [''.join(p.itertext()) for p in document.findall('w:body/w:p',NS)]
            assert all(any(line in paragraph for paragraph in paragraphs) for line in text.split('\n'))
            assert not any(text.split('\n')[0] in paragraph and text.split('\n')[1] in paragraph for paragraph in paragraphs)
            size = document.find('.//w:sectPr/w:pgSz',NS)
            assert abs(int(size.get('{'+NS['w']+'}w'))/20-expected[0]) < .06
            settings = ET.fromstring(package.read('word/settings.xml'))
            update = settings.find('w:updateFields',NS)
            assert update is None or update.get('{'+NS['w']+'}val') in ('false','0')
            assert any(name.startswith('word/header') for name in package.namelist())
            assert any(name.startswith('word/footer') for name in package.namelist())
        out = ROOT/'Tests/generated'; out.mkdir(exist_ok=True)
        (out/(filename+'.docx')).write_bytes(docx); (out/(filename+'.pdf')).write_bytes(pdf)
        (out/(filename+'.json')).write_bytes(author.raw(download+'&format=json')[0])
    author.json('/templates/starter','POST',{'title':'Invalid paper','kind':'letter','language':'fa-IR','paper':'A3'},expected=400)
    print('PASS: Persian A5/A4 typography and English block-letter geometry, script fonts/sizes, all bookmarks, header/footer composition, signature/branding images, English date and real DOCX/PDF exports.')
