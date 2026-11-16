import importlib.util, pathlib, unittest
spec=importlib.util.spec_from_file_location('release',pathlib.Path(__file__).with_name('release.py'));release=importlib.util.module_from_spec(spec);spec.loader.exec_module(release)
class ReleaseTests(unittest.TestCase):
 def test_monotonic_numeric_versions(self):
  self.assertGreater(release.numeric('0.10.0'),release.numeric('0.9.9'))
  for invalid in ('v1.0.0','1.0','1.0.0-beta','01.0.0','../1.2.3'):
   with self.assertRaises(ValueError):release.numeric(invalid)
 def test_product_metadata(self):
  meta=release.metadata();self.assertEqual(meta['PackageName'],'xiaoheihe-community-formac');self.assertEqual(meta['DisplayName'],'小黑盒Community For Mac');self.assertEqual(meta['BundleId'],'com.3egirlsdream.xiaoheihe-community-formac');self.assertEqual(meta['TagPrefix'],'mac-v')
if __name__=='__main__':unittest.main()
