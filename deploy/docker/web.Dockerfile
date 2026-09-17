FROM node:24-alpine AS build
WORKDIR /app
COPY frontend/trading-platform-ui/package*.json ./
RUN npm ci
COPY frontend/trading-platform-ui/ ./
RUN npm run build -- --configuration=production

FROM nginx:1.27-alpine
COPY deploy/nginx/nginx.conf /etc/nginx/nginx.conf
COPY --from=build /app/dist/trading-platform-ui/browser /usr/share/nginx/html
EXPOSE 80
