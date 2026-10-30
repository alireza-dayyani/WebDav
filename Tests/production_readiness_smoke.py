"""Real cookie/CSRF coverage for the first production-readiness application batch."""
import os
from unittest.mock import patch
from studio_test_support import Client, server

with server(5097, 'readiness-privacy') as (base, _):
    alice, bob = Client(base), Client(base)
    alice.register('readiness-alice'); bob.register('readiness-bob')
    assert [u['email'] for u in alice.json('/letters/directory')['items']] == ['readiness-alice@example.test']
    assert alice.json('/letters/directory?search=readiness')['items'] == []
    assert alice.json('/letters/directory?page=2')['items'] == []
    assert [u['email'] for u in alice.json('/letters/directory?search=READINESS-BOB%40EXAMPLE.TEST')['items']] == ['readiness-bob@example.test']
    document = alice.json('/documents/', 'POST', {'title': 'Isolated capacity', 'language': 'en-US'}, expected=201)
    alice.json(f'/documents/{document["id"]}/content', 'PUT', {'version': document['version'], 'document': {'body': [{'kind': 'para', 'text': 'Validated save', 'runs': []}]}})
    bob.json(f'/documents/{document["id"]}/content', expected=404)

with patch.dict(os.environ, {'Traffic__AuthenticatedPerMinute': '3'}):
    with server(5097, 'readiness-traffic') as (base, _):
        alice, bob = Client(base), Client(base)
        alice.register('budget-alice')  # One authenticated directory request.
        alice.json('/account/me'); alice.json('/account/me')
        content, _, headers = alice.raw('/api/account/me', expected=429)
        assert b'rate_limited' in content
        assert int(headers['Retry-After']) > 0
        alice.json('/health')
        bob.register('budget-bob')
        bob.json('/account/me')  # Alice's exhausted budget does not affect Bob.

print('PASS: exact recipient lookup, private native saves, real per-account 429/Retry-After and independent account budgets.')
