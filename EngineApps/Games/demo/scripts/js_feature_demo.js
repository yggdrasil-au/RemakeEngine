// JavaScript feature showcase for the RemakeEngine demo module.
// Demonstrates helpers available to Jint scripts: argv parsing, events,
// progress reporting, SDK utilities, sqlite access, and JSON helpers.

const args = Array.isArray(argv) ? argv.slice() : [];

function pickValue(flag) {
    const index = args.indexOf(flag);
    if (index >= 0 && index + 1 < args.length) {
        return args[index + 1];
    }
    return null;
}

function hasFlag(flag) {
    return args.indexOf(flag) >= 0;
}

const moduleRoot = pickValue('--module') ?? args[0] ?? '.';
const scratchRoot = pickValue('--scratch') ?? `${moduleRoot}/TMP/js-demo`;
const note = pickValue('--note') ?? 'JavaScript demo note (default)';
const withExtra = hasFlag('--with-extra');

emit('info', { language: 'js', step: 'start', moduleRoot });

sdk.ensure_dir(scratchRoot);
sdk.ensure_dir(`${scratchRoot}/artifacts`);
sdk.colour_print({ color: 'cyan', message: `JavaScript demo scratch workspace: ${scratchRoot}` });

const progressHandle = progress(3, 'js-demo', 'JavaScript feature showcase');
progressHandle.Update();

const md5Hash = sdk.md5(note);
const lfs = require('lfs');
const iterator = lfs.dir(moduleRoot);
const sampleEntries = [];
while (true) {
    const entry = iterator();
    if (entry === null || entry === undefined) {
        break;
    }
    sampleEntries.push(entry);
    if (sampleEntries.length >= 5) {
        break;
    }
}
progressHandle.Update();

const dbPath = `${scratchRoot}/js_demo.sqlite`;
const db = sqlite.open(dbPath);
db.exec('CREATE TABLE IF NOT EXISTS feature_log (id INTEGER PRIMARY KEY AUTOINCREMENT, category TEXT, message TEXT)');
db.exec('INSERT INTO feature_log(category, message) VALUES (:category, :message)', { category: 'note', message: note });
db.exec('INSERT INTO feature_log(category, message) VALUES (:category, :message)', { category: 'hash', message: md5Hash });
if (withExtra) {
    db.exec('INSERT INTO feature_log(category, message) VALUES (:category, :message)', { category: 'extra', message: 'Extra artifact requested via CLI flag' });
}
const rows = db.query('SELECT id, category, message FROM feature_log ORDER BY id ASC');
for (let i = 0; i < rows.length; i++) {
    const row = rows[i];
    emit('info', { language: 'js', entry: row });
}
db.close();

const json = require('dkjson');
const summary = {
    language: 'js',
    moduleRoot,
    scratch: scratchRoot,
    note,
    md5: md5Hash,
    sampleEntries,
    withExtra,
    sqlitePath: dbPath,
    timestamp: new Date().toISOString()
};
const encoded = json.encode(summary, { indent: true });
sdk.colour_print({ color: 'green', message: encoded });

progressHandle.Update();
emit('js-demo-complete', {
    language: 'js',
    artifacts: {
        sqlite: dbPath
    },
    extra: withExtra
});
