import sqlite3,sys,tempfile,unittest
from contextlib import closing
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from heaven_database import DatabasePlugin

class DatabaseTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory(); self.root=Path(self.tmp.name)
        self.db=self.root/"x.db"
        with closing(sqlite3.connect(self.db)) as c:
            c.execute("create table t(id integer primary key,v text)")
            c.execute("insert into t(v) values ('a')")
            c.commit()
        self.p=DatabasePlugin(self.root)
    def tearDown(self): self.tmp.cleanup()

    def test_query_is_read_only(self):
        with self.assertRaises(sqlite3.OperationalError):
            self.p.query("x.db","delete from t")
    def test_query_returns_rows(self):
        r=self.p.query("x.db","select * from t")
        self.assertEqual(r["rows"][0]["v"],"a")
    def test_execute_requires_confirmation(self):
        with self.assertRaises(ValueError): self.p.execute("x.db","delete from t")
    def test_backup_within_root(self):
        r=self.p.backup("x.db","backup.db",confirm=True)
        self.assertTrue(Path(r["destination"]).exists())
    def test_path_escape_rejected(self):
        with self.assertRaises(ValueError): self.p.schema("../outside.db")
    def test_plugin_releases_database_handle(self):
        self.p.schema("x.db")
        moved=self.root/"moved.db"
        self.db.replace(moved)
        moved.replace(self.db)

if __name__=="__main__":unittest.main()
