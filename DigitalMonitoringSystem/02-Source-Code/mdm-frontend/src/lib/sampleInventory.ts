import sampleInventoryCsv from '../data/sample-inventory.csv?raw'

export type SampleInventoryAsset = {
  assetId: string
  deviceType: string
  brand: string
  model: string
  serialNumber: string
  department: string
  location: string
  assignedTo: string
  purchaseDate: string
  condition: string
  recordStatus: string
  notes: string
}

function parseCsv(csv: string): string[][] {
  const rows: string[][] = []
  let row: string[] = []
  let field = ''
  let quoted = false

  for (let index = 0; index < csv.length; index += 1) {
    const character = csv[index]

    if (quoted) {
      if (character === '"' && csv[index + 1] === '"') {
        field += '"'
        index += 1
      } else if (character === '"') {
        quoted = false
      } else {
        field += character
      }
    } else if (character === '"' && field.length === 0) {
      quoted = true
    } else if (character === ',') {
      row.push(field)
      field = ''
    } else if (character === '\n') {
      row.push(field.replace(/\r$/, ''))
      if (row.some((value) => value.length > 0)) rows.push(row)
      row = []
      field = ''
    } else {
      field += character
    }
  }

  if (quoted) {
    throw new Error('The sample inventory CSV contains an unclosed quoted field.')
  }

  if (field.length > 0 || row.length > 0) {
    row.push(field.replace(/\r$/, ''))
    if (row.some((value) => value.length > 0)) rows.push(row)
  }

  return rows
}

const columns = [
  'Asset ID',
  'Device Type',
  'Brand (sample)',
  'Model',
  'Serial Number',
  'Department (sample allocation)',
  'Office / Location (sample)',
  'Assigned To (fictional)',
  'Purchase Date (sample)',
  'Condition (sample)',
  'Record Status',
  'Notes',
] as const

function loadSampleInventory(csv: string): SampleInventoryAsset[] {
  const [header, ...records] = parseCsv(csv.replace(/^\uFEFF/, ''))
  if (!header) {
    throw new Error('The sample inventory CSV is empty.')
  }

  const indexes = new Map(header.map((name, index) => [name, index]))
  const missingColumns = columns.filter((name) => !indexes.has(name))
  if (missingColumns.length > 0) {
    throw new Error(`The sample inventory CSV is missing columns: ${missingColumns.join(', ')}`)
  }

  const get = (record: string[], name: (typeof columns)[number]) => record[indexes.get(name)!] ?? ''
  const assets = records.map((record) => ({
    assetId: get(record, 'Asset ID'),
    deviceType: get(record, 'Device Type'),
    brand: get(record, 'Brand (sample)'),
    model: get(record, 'Model'),
    serialNumber: get(record, 'Serial Number'),
    department: get(record, 'Department (sample allocation)'),
    location: get(record, 'Office / Location (sample)'),
    assignedTo: get(record, 'Assigned To (fictional)'),
    purchaseDate: get(record, 'Purchase Date (sample)'),
    condition: get(record, 'Condition (sample)'),
    recordStatus: get(record, 'Record Status'),
    notes: get(record, 'Notes'),
  }))

  const seenAssetIds = new Set<string>()
  for (const asset of assets) {
    if (!asset.assetId || seenAssetIds.has(asset.assetId)) {
      throw new Error(`The sample inventory contains a missing or duplicate asset ID: ${asset.assetId || '(blank)'}`)
    }
    if (!asset.recordStatus.startsWith('SAMPLE') || !asset.notes.includes('Fictional test record')) {
      throw new Error(`Inventory record ${asset.assetId} is not explicitly marked as sample data.`)
    }
    seenAssetIds.add(asset.assetId)
  }

  return assets
}

export { sampleInventoryCsv }
export const sampleInventory = loadSampleInventory(sampleInventoryCsv)
