// Bug report receiver — accepts both JSON and multipart, forwards to Discord
const DISCORD_WEBHOOK = process.env.DISCORD_BUG_REPORT_WEBHOOK || '';

module.exports = async function handler(req, res) {
  if (req.method !== 'POST') {
    return res.status(405).json({ error: 'POST required' });
  }

  if (!DISCORD_WEBHOOK) {
    return res.status(500).json({ error: 'Webhook not configured on server' });
  }

  res.setHeader('Access-Control-Allow-Origin', '*');

  try {
    let content = '';

    const ct = (req.headers['content-type'] || '').toLowerCase();
    if (ct.includes('multipart/form-data')) {
      // Busboy or simple parsing — Vercel parses multipart automatically
      const fields = {};
      for (const key of Object.keys(req.body || {})) {
        if (typeof req.body[key] === 'string') fields[key] = req.body[key];
      }
      content = fields.content || '';
    } else {
      content = (req.body && req.body.content) || '';
    }

    if (!content) {
      return res.status(400).json({ error: 'Missing content field' });
    }

    const resp = await fetch(DISCORD_WEBHOOK, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ content, username: 'SpokysPL Bug Report' })
    });

    if (!resp.ok) {
      const text = await resp.text();
      return res.status(resp.status).json({ error: text });
    }

    res.status(200).json({ ok: true });
  } catch (err) {
    res.status(500).json({ error: err.message });
  }
};
