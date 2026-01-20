/** @type {import('tailwindcss').Config} */
const path = require('path');

module.exports = {
  content: [
    path.resolve(__dirname, 'wwwroot/index.html'),
    path.resolve(__dirname, 'wwwroot/js/app.js'),
  ],
  theme: {
    extend: {
      fontFamily: {
        sans: ['-apple-system', 'BlinkMacSystemFont', '"Segoe UI"', 'Roboto', '"Helvetica Neue"', 'Arial', 'sans-serif'],
      },
    },
  },
  plugins: [],
}
