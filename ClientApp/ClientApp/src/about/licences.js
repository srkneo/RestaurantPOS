// Free open-source software inside the POS, shown on the About page.
// Only licences that cost nothing and allow use in a paid product are allowed (see licences.test.js).
export const FREE_LICENCES = ['MIT', 'Apache-2.0', 'BSD-3-Clause', 'OFL-1.1', 'PostgreSQL', 'CC-BY-4.0'];

export const LICENCES = [
  // Screen (browser)
  { name: 'react', part: 'web', licence: 'MIT', url: 'https://github.com/facebook/react' },
  { name: 'react-dom', part: 'web', licence: 'MIT', url: 'https://github.com/facebook/react' },
  { name: 'react-router-dom', part: 'web', licence: 'MIT', url: 'https://github.com/remix-run/react-router' },
  { name: 'bootstrap', part: 'web', licence: 'MIT', url: 'https://github.com/twbs/bootstrap' },
  { name: '@microsoft/signalr', part: 'web', licence: 'MIT', url: 'https://github.com/dotnet/aspnetcore' },
  { name: '@fontsource/inter', part: 'web', licence: 'OFL-1.1', url: 'https://github.com/fontsource/font-files' },
  { name: '@fontsource/space-grotesk', part: 'web', licence: 'OFL-1.1', url: 'https://github.com/fontsource/font-files' },
  { name: '@fortawesome/fontawesome-svg-core', part: 'web', licence: 'MIT', url: 'https://github.com/FortAwesome/Font-Awesome' },
  { name: '@fortawesome/free-solid-svg-icons', part: 'web', licence: 'CC-BY-4.0', url: 'https://fontawesome.com/license/free' },
  { name: '@fortawesome/react-fontawesome', part: 'web', licence: 'MIT', url: 'https://github.com/FortAwesome/react-fontawesome' },
  // Server
  { name: '.NET and ASP.NET Core', part: 'server', licence: 'MIT', url: 'https://github.com/dotnet/aspnetcore' },
  { name: 'Entity Framework Core', part: 'server', licence: 'MIT', url: 'https://github.com/dotnet/efcore' },
  { name: 'Npgsql.EntityFrameworkCore.PostgreSQL', part: 'server', licence: 'PostgreSQL', url: 'https://github.com/npgsql/efcore.pg' },
  { name: 'NLog.Web.AspNetCore', part: 'server', licence: 'BSD-3-Clause', url: 'https://github.com/NLog/NLog.Web' },
  { name: 'NLog.Targets.Seq', part: 'server', licence: 'Apache-2.0', url: 'https://github.com/datalust/nlog-targets-seq' },
  { name: 'QRCoder', part: 'server', licence: 'MIT', url: 'https://github.com/codebude/QRCoder' },
  { name: 'Scalar.AspNetCore', part: 'server', licence: 'MIT', url: 'https://github.com/scalar/scalar' },
  { name: 'PostgreSQL', part: 'server', licence: 'PostgreSQL', url: 'https://www.postgresql.org/about/licence/' },
];
