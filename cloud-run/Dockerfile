FROM node:22-alpine
ENV NODE_ENV=production
WORKDIR /app
COPY package.json model.mjs server.mjs ./
USER node
EXPOSE 8080
CMD ["node","server.mjs"]
