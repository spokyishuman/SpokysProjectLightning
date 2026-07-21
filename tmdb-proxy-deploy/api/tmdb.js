const TMDB_BASE = 'https://api.themoviedb.org/3';
const API_KEY = process.env.TMDB_API_KEY || '03ea17fd725585fa30751965ed1993eb';

module.exports = async function handler(req, res) {
  const path = req.query.path || '';
  if (!path) {
    return res.status(200).json({ status: 'ok', endpoints: ['/api/tmdb?path=trending/all/week'] });
  }

  const idx = req.url.indexOf('?');
  const qs = idx >= 0
    ? req.url.slice(idx + 1).split('&').filter(p => !p.startsWith('path=')).join('&')
    : '';
  const sep = qs ? '&' : '';
  const url = `${TMDB_BASE}/${path}?${qs}${sep}api_key=${API_KEY}`;

  try {
    const resp = await fetch(url, {
      headers: { 'User-Agent': 'SpokysPL-TMDB-Proxy/1.0', 'Accept': 'application/json' }
    });
    const body = await resp.text();
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('Cache-Control', 'public, max-age=300');
    res.status(resp.status).send(body);
  } catch (err) {
    res.status(500).json({ error: err.message });
  }
};
