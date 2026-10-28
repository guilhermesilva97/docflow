FROM node:22-alpine AS deps
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund

FROM deps AS test
COPY . .
CMD ["sh", "-c", "npx prettier --check src && npx ng test --watch=false && npx ng build"]

FROM deps AS build
COPY . .
RUN npx ng build

FROM nginx:1.27-alpine AS runtime
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/web/browser /usr/share/nginx/html
EXPOSE 80
