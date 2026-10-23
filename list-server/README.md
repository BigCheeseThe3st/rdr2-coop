# Room list for the server browser

A Cloudflare Worker (free plan) holding the rooms of hosts who chose
`Public=Yes`. Nothing is stored on disk: one Durable Object keeps the list in
memory, rooms re-announce every minute and drop off 3 minutes after they stop.

    npx wrangler dev        # local test on http://localhost:8787
    npx wrangler deploy     # publish (then set ListServer= in RDR2Coop.ini to its URL)

Abuse limits: a room is always listed under the address it announced from
(private and shared addresses are refused), at most 3 rooms per address,
1000 in total, names cut to 32 printable characters.
