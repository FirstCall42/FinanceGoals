# syntax=docker/dockerfile:1
# The web image: the built Angular app, served by unprivileged nginx on 8080. Docker Compose (#13)
# runs it as is; the deploy workflow (#21) copies /usr/share/nginx/html out of the same image to
# Static Web Apps, so every environment gets the identical build. Build from the repository root:
#   docker build -f web/Dockerfile -t financegoals-web .

FROM node:24-alpine AS build
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN --mount=type=cache,target=/root/.npm npm ci
COPY web/ ./
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.30-alpine
COPY web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /web/dist/financegoals-web/browser /usr/share/nginx/html
EXPOSE 8080
