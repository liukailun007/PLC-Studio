/**
 * Manual installer for the bundled binaries (fallback for machines that cannot
 * reach GitHub reliably, e.g. mainland China).
 *
 * Usage:
 *   1. Download the missing archives with a BROWSER (it resumes broken downloads)
 *      and save them into resources/binaries/<platform>-<arch>/downloads/ using
 *      the exact filenames below (uv.zip, rg.zip, mingit.zip, ...).
 *   2. Run:  node scripts/install-bundled-binaries-from-local.js [platform] [arch]
 *
 * The script reuses download-binaries.js' TOOLS table (URLs + SHA256 checksums)
 * and extract logic, so verification and layout are identical to the online path.
 */
const fs = require('fs')
const path = require('path')
const crypto = require('crypto')

const { TOOLS, extract } = require('./download-binaries')

const platform = process.argv[2] || process.platform
const arch = process.argv[3] || process.arch
const platformKey = `${platform}-${arch}`

const outputDir = path.join(__dirname, '..', 'resources', 'binaries', platformKey)
const downloadsDir = path.join(outputDir, 'downloads')
fs.mkdirSync(outputDir, { recursive: true })
fs.mkdirSync(downloadsDir, { recursive: true })

let hadError = false

for (const tool of TOOLS) {
  const pkg = tool.packages[platformKey]
  if (!pkg) continue

  const versionPath = path.join(outputDir, tool.versionFile)
  const binaryPaths = pkg.binaries.map((b) => path.join(outputDir, b))
  const upToDate =
    fs.existsSync(versionPath) &&
    binaryPaths.every((b) => fs.existsSync(b)) &&
    fs.readFileSync(versionPath, 'utf8').trim() === tool.version
  if (upToDate) {
    console.log(`[${tool.name}] ${tool.version} already installed — skipping`)
    continue
  }

  const ext = pkg.archive === 'tar.gz' ? 'tar.gz' : 'zip'
  const archivePath = path.join(downloadsDir, `${tool.name}.${ext}`)
  if (!fs.existsSync(archivePath)) {
    console.error(`[${tool.name}] MISSING archive: ${archivePath}`)
    console.error(`   Download this in your browser and save it with that exact name:`)
    console.error(`   ${pkg.url}`)
    hadError = true
    continue
  }

  const hash = crypto.createHash('sha256').update(fs.readFileSync(archivePath)).digest('hex')
  if (hash !== pkg.sha256) {
    console.error(
      `[${tool.name}] SHA256 mismatch — file is corrupt or incomplete. Re-download it.`
    )
    console.error(`   expected ${pkg.sha256}`)
    console.error(`   got      ${hash}`)
    hadError = true
    continue
  }

  extract(archivePath, pkg.archive, outputDir, pkg)
  fs.unlinkSync(archivePath)
  if (process.platform !== 'win32') {
    for (const b of pkg.binaries) fs.chmodSync(path.join(outputDir, b), 0o755)
  }
  fs.writeFileSync(versionPath, tool.version, 'utf8')
  console.log(`[${tool.name}] Installed ${pkg.binaries.join(', ')} ${tool.version}`)
}

if (hadError) {
  console.error('\nSome binaries are still missing — fix the items above, then re-run this script.')
  process.exit(1)
}
console.log(`\nAll bundled binaries for ${platformKey} are in place.`)
