module.exports = {
  content: ["./Views/**/*.cshtml", "./wwwroot/**/*.js"],
  theme: { extend: {} },
  plugins: [require('@tailwindcss/line-clamp')],
}
