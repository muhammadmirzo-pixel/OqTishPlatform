# syntax=docker/dockerfile:1
ARG NODE_IMAGE=node:24-alpine
ARG NGINX_IMAGE=nginxinc/nginx-unprivileged:1.28-alpine

FROM ${NODE_IMAGE} AS build
WORKDIR /web
COPY web/package.json web/package-lock.json web/.npmrc ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npm run build

# Unprivileged nginx (uid 101, port 8080): serves the SPA and reverse-proxies the API.
FROM ${NGINX_IMAGE} AS web
COPY deploy/nginx/security-headers.conf /etc/nginx/snippets/security-headers.conf
COPY deploy/nginx/default.conf.template /etc/nginx/templates/default.conf.template
COPY --chmod=0755 deploy/nginx/40-write-config-json.sh /docker-entrypoint.d/40-write-config-json.sh
# Static files stay root-owned (read-only for the nginx user).
COPY --from=build /web/dist /usr/share/nginx/html
ENV OQTISH_API_UPSTREAM=api:8080 OQTISH_WEB_LISTEN=8080 OQTISH_PUBLIC_API_BASE_URL=/api
EXPOSE 8080
