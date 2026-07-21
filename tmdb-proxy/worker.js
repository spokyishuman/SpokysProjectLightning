// TMDB API Proxy — Cloudflare Worker
// Deploy: copy to https://dash.cloudflare.com → Workers & Pages → Create Worker
// Add TMDB_API_KEY as environment variable (Settings → Variables)
// Then set ProxyUrl in app Settings to your worker URL

const TMDB_BASE = 'https://api.themoviedb.org/3';

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const path = url.pathname;

    if (path === '/' || path === '') {
      return new Response(JSON.stringify({ status: 'ok', endpoints: ['/3/*'] }), {
        headers: { 'content-type': 'application/json', 'access-control-allow-origin': '*' }
      });
    }

    const tmdbUrl = `${TMDB_BASE}${path}?${url.searchParams.toString()}&api_key=${env.TMDB_API_KEY}`;

    try {
      const resp = await fetch(tmdbUrl, {
        headers: {
          'User-Agent': 'SpokysPL-TMDB-Proxy/1.0',
          'Accept': 'application/json'
        }
      });
      const body = await resp.text();
      return new Response(body, {
        status: resp.status,
        headers: {
          'content-type': 'application/json',
          'access-control-allow-origin': '*',
          'cache-control': 'public, max-age=300'
        }
      });
    } catch (err) {
      return new Response(JSON.stringify({ error: err.message }), {
        status: 500,
        headers: { 'content-type': 'application/json', 'access-control-allow-origin': '*' }
      });
    }
  }
};
