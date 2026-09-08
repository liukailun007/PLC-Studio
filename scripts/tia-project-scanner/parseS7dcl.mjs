/**
 * Parse Siemens SIMATIC document exports (.s7dcl + .s7res) into structured block
 * interface info (per member: name, direction INPUT/OUTPUT/IN_OUT/STATIC/TEMP,
 * type, optional Chinese comment). Reading text via UTF-8 avoids the MCP-side
 * mojibake bug in DescribeBlockLogic/LadTextRenderer.
 */

// id -> zh-CN text, from .s7res
function readMlc(s7res) {
  const map = {}
  if (!s7res) return map
  const re = /id:\s*(\S+)[\s\S]*?zh-CN:\s*(.+)/g
  let m
  while ((m = re.exec(s7res)) !== null) map[m[1].trim()] = m[2].trim()
  return map
}

const KIND_BY_DECL = {
  FUNCTION_BLOCK: 'FB',
  FUNCTION: 'FC',
  ORGANIZATION_BLOCK: 'OB',
  DATA_BLOCK: 'DB',
  TYPE: 'UDT'
}

/**
 * @param {string} s7dcl        content of a .s7dcl file
 * @param {string} [s7res=""]   content of the sibling .s7res
 * @returns {{blockName:string|null, kind:string|null, members:Array}}
 */
export function parseS7dcl(s7dcl, s7res = '') {
  const res = { blockName: null, kind: null, members: [] }
  const mlc = readMlc(s7res)

  const decl = /^(FUNCTION_BLOCK|FUNCTION|ORGANIZATION_BLOCK|DATA_BLOCK|TYPE)\s+(?:"?([^"\s]+)"?)?/m.exec(s7dcl)
  if (decl) {
    res.kind = KIND_BY_DECL[decl[1]] || decl[1]
    res.blockName = decl[2] || null
  }

  // cut anything starting at the logic NETWORK section (NETWORK may be indented)
  const logicMatch = /^\s*NETWORK\b/m.exec(s7dcl)
  const code = logicMatch ? s7dcl.slice(0, logicMatch.index) : s7dcl
  const lines = code.split(/\r?\n/)
  let dir = 'STATIC'
  let pendingId = null
  for (const raw of lines) {
    const t = raw.trim()
    if (/^END_(FUNCTION_BLOCK|FUNCTION|DATA_BLOCK|TYPE|VAR|CONSTANT)/.test(t)) continue
    if (/^VAR_CONSTANT/.test(t)) dir = 'CONSTANT'
    else if (/^VAR_IN_OUT/.test(t)) dir = 'IN_OUT'
    else if (/^VAR_INPUT/.test(t)) dir = 'INPUT'
    else if (/^VAR_OUTPUT/.test(t)) dir = 'OUTPUT'
    else if (/^VAR_TEMP/.test(t)) dir = 'TEMP'
    else if (/^VAR_STATIC/.test(t)) dir = 'STATIC'
    else if (/^\s*VAR\s*$/.test(t)) { dir = 'STATIC' }  // bare VAR (static block) always STATIC
    // an attribute-only line like { S7_MLC := "id" } precedes its member line
    const attrOnly = /^\{\s*S7_MLC\s*:=\s*"([\w]+)"\s*\}\s*$/.exec(t)
    if (attrOnly) { pendingId = attrOnly[1]; continue }
    // member:  NAME : Type[;]
    const mm = /^([A-Za-z_#][\w#]*)\s*:\s*([\w.]+)(?:\s*;)?/.exec(t)
    if (mm) {
      const id = pendingId
      pendingId = null
      res.members.push({
        name: mm[1].replace(/^#/, ''),
        dir,
        type: mm[2],
        mlcId: id,
        comment: id ? mlc[id] || null : null
      })
    } else if (!/^\{|^#/.test(t)) {
      // not an attribute/member/heading — reset pending only at section switches (handled above)
    }
  }
  return res
}

/**
 * Readable preview of SCL logic body from an .s7dcl (UTF-8 read) if present.
 */
export function logicPreview(s7dcl, max = 6000) {
  // find a NETWORK / END_FUNCTION_BLOCK brace to delimit the body; NETWORK may be indented
  const m = /^\s*NETWORK\b/m.exec(s7dcl)
  if (!m) return null
  return s7dcl.slice(m.index + m[0].length).replace(/^\s+/, '').slice(0, max)
}
