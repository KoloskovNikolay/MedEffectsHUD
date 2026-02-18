import json
import os
import shutil
from pathlib import Path

# Paths
mod_path = Path(r"E:\STP4.0.10\SPT\user\mods\SalcosArsenal")
ammo_path = mod_path / "db" / "CustomAmmo"
armor_path = mod_path / "db" / "CustomArmor" / "Body_armor_build_ins"

def boost_ammo():
    """Boost all ammo by 40%"""
    print("=== Boosting Ammunition ===")
    ammo_files = list(ammo_path.glob("*.json"))
    
    for ammo_file in ammo_files:
        print(f"Processing: {ammo_file.name}")
        with open(ammo_file, 'r', encoding='utf-8') as f:
            data = json.load(f)
        
        # Modify each item in the file
        for item_id, item_data in data.items():
            props = item_data.get("overrideProperties", {})
            
            # Boost damage properties by 40%
            damage_props = ["Damage", "ArmorDamage", "PenetrationPower", "InitialSpeed"]
            for prop in damage_props:
                if prop in props:
                    old_val = props[prop]
                    new_val = int(old_val * 1.4)
                    props[prop] = new_val
                    print(f"  {prop}: {old_val} -> {new_val}")
        
        # Save modified file
        with open(ammo_file, 'w', encoding='utf-8') as f:
            json.dump(data, f, indent=2, ensure_ascii=False)
    
    print(f"✓ Boosted {len(ammo_files)} ammo files\n")

def boost_level6_armor():
    """Boost Level 6 armor by 80% durability"""
    print("=== Boosting Level 6 Armor ===")
    level6_path = armor_path / "Level_6"
    
    if not level6_path.exists():
        print("! Level_6 folder not found")
        return
    
    armor_files = list(level6_path.glob("*.json"))
    
    for armor_file in armor_files:
        print(f"Processing: {armor_file.name}")
        with open(armor_file, 'r', encoding='utf-8') as f:
            data = json.load(f)
        
        # Modify each item in the file
        for item_id, item_data in data.items():
            props = item_data.get("overrideProperties", {})
            
            # Boost durability by 80%
            if "Durability" in props:
                old_dur = props["Durability"]
                new_dur = int(old_dur * 1.8)
                props["Durability"] = new_dur
                print(f"  Durability: {old_dur} -> {new_dur}")
            
            if "MaxDurability" in props:
                old_max = props["MaxDurability"]
                new_max = int(old_max * 1.8)
                props["MaxDurability"] = new_max
                print(f"  MaxDurability: {old_max} -> {new_max}")
        
        # Save modified file
        with open(armor_file, 'w', encoding='utf-8') as f:
            json.dump(data, f, indent=2, ensure_ascii=False)
    
    print(f"✓ Boosted {len(armor_files)} Level 6 armor files\n")

def create_level7_armor():
    """Copy Level 6 armor and create Level 7 versions"""
    print("=== Creating Level 7 Armor ===")
    level6_path = armor_path / "Level_6"
    level7_path = armor_path / "Level_7"
    
    # Create Level_7 folder if it doesn't exist
    level7_path.mkdir(exist_ok=True)
    
    armor_files = list(level6_path.glob("*.json"))
    
    for armor_file in armor_files:
        print(f"Creating Level 7 from: {armor_file.name}")
        with open(armor_file, 'r', encoding='utf-8') as f:
            data = json.load(f)
        
        # Modify each item to make it Level 7
        new_data = {}
        for item_id, item_data in data.items():
            # Generate new ID (change first few characters)
            new_id = "7" + item_id[1:]
            
            # Deep copy item data
            new_item = json.loads(json.dumps(item_data))
            
            # Modify properties for Level 7
            props = new_item.get("overrideProperties", {})
            
            # Set armor class to 7
            props["armorClass"] = "7"
            
            # Increase durability by another 50% over Level 6 boosted values
            if "Durability" in props:
                props["Durability"] = int(props["Durability"] * 1.5)
            if "MaxDurability" in props:
                props["MaxDurability"] = int(props["MaxDurability"] * 1.5)
            
            # Reduce BluntThroughput if present (better protection)
            if "BluntThroughput" in props:
                props["BluntThroughput"] = props["BluntThroughput"] * 0.7
            
            # Update locales
            locales = new_item.get("locales", {})
            for lang, locale_data in locales.items():
                if "name" in locale_data:
                    locale_data["name"] = locale_data["name"].replace("Lv.6", "Lv.7").replace("Lv6", "Lv7")
                if "shortName" in locale_data:
                    locale_data["shortName"] = locale_data["shortName"].replace("6", "7")
            
            # Update prices (make it more expensive)
            if "fleaPriceRoubles" in new_item:
                new_item["fleaPriceRoubles"] = int(new_item["fleaPriceRoubles"] * 1.5)
            if "handbookPriceRoubles" in new_item:
                new_item["handbookPriceRoubles"] = int(new_item["handbookPriceRoubles"] * 1.5)
            
            new_data[new_id] = new_item
            print(f"  Created: {new_id} (armorClass 7)")
        
        # Save Level 7 file
        new_filename = armor_file.name.replace("Lv6", "Lv7").replace("_6", "_7")
        new_file_path = level7_path / new_filename
        with open(new_file_path, 'w', encoding='utf-8') as f:
            json.dump(new_data, f, indent=2, ensure_ascii=False)
        print(f"  Saved: {new_file_path.name}")
    
    print(f"✓ Created {len(armor_files)} Level 7 armor files\n")

def main():
    print("SalcoArsenal Mod Enhancement Script")
    print("=" * 50)
    print()
    
    # 1. Boost ammo by 40%
    boost_ammo()
    
    # 2. Boost Level 6 armor by 80%
    boost_level6_armor()
    
    # 3. Create Level 7 armor
    create_level7_armor()
    
    print("=" * 50)
    print("✓ ALL MODIFICATIONS COMPLETE!")
    print()
    print("Changes made:")
    print("  • All ammunition boosted by 40%")
    print("  • Level 6 armor durability boosted by 80%")
    print("  • Level 7 armor created from Level 6")

if __name__ == "__main__":
    main()
