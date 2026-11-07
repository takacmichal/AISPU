# Build context: frontend/
#   docker build -t solarops-web --build-arg VITE_API_URL=https://<api>.azurecontainerapps.io frontend
FROM node:22-alpine AS build
WORKDIR /app
RUN corepack enable

# Install first for better layer caching.
COPY package.json pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile

COPY . .
# Public URL of the API, baked into the bundle (one environment, no runtime config: ADR #277).
ARG VITE_API_URL
ENV VITE_API_URL=$VITE_API_URL
RUN pnpm build

FROM nginxinc/nginx-unprivileged:1.30-alpine AS runtime
# Origin allowed in CSP connect-src; Azure sets it to the exact API origin.
ENV API_ORIGIN=https://*.azurecontainerapps.io
COPY nginx/default.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 8080
