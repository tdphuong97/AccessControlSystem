# acs-sync (Cloudflare Worker + D1)

Shared data for Access Control System ("Dữ liệu chung"). PCs encrypt every row with a key only they hold; this
Worker stores ciphertext, orders writes with a global version number and hands out the "sender" lease.

Deploy (account vienba.cloudflare@gmail.com, from this folder):

    npx wrangler d1 create acs-sync                 # copy database_id into wrangler.toml
    npx wrangler d1 execute acs-sync --remote --file=schema.sql
    npx wrangler deploy
    npx wrangler secret put CONNECT_KEY             # 32 random bytes, base64url

Connection code for the app: `acs1:acs-sync.<subdomain>.workers.dev:<CONNECT_KEY>`. Changing the key = put a new
secret, then enter the new code on every PC.

Tests: `node --test test/*.test.mjs` (Node 24, node:sqlite stands in for D1).
