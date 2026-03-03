FROM node:22-alpine AS build
WORKDIR /app

COPY src/frontend/fluxocaixa-web/package*.json ./
RUN npm install

COPY src/frontend/fluxocaixa-web/ .
RUN npm run build

FROM nginx:1.27-alpine AS runtime

RUN rm /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/fluxocaixa-web/browser /usr/share/nginx/html

RUN printf 'server {\n\
    listen 80;\n\
    root /usr/share/nginx/html;\n\
    index index.html;\n\
    location / {\n\
        try_files $uri $uri/ /index.html;\n\
    }\n\
    location /health {\n\
        return 200 "ok";\n\
        add_header Content-Type text/plain;\n\
    }\n\
}\n' > /etc/nginx/conf.d/fluxocaixa.conf

EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
