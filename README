## gitadora-texbintool.exe
Convert archives of tex images.
```
usage: gitadora-texbintool.exe [--no-rect/--nr] [--no-split/--ns] input_filename
--no-rect/--nr: Don't create a rect table (Some games like Jubeat don't use the rect table)
--no-split/--ns: Don't split images into separate images if they use the rect table
```

If you specify a .bin file as the `input_filename` then the tool will extract the textures into a folder with the same name as the .bin file.
If you specify a folder as the `input_filename` then the tool will create a .bin file with the same name as the folder.

`--no-rect` is used for .bin creation. It skips writing the rect section at the end of the bin file.

`--no-split` is used during .bin extraction. Texbins can have multiple files, and within those files have multiple rects/subimages.
This command will output the original images with a `metadata.xml` file containing the rect information.
When creating a .bin from a folder with a `metadata.xml`, the `metadata.xml` is used to create the .bin. Any files in the folder not listed in the `metadata.xml` will be ignored.
If you want to replace a specific subimage without modifying the original image file, you can modify the `ExternalFilename` part of the `metadata.xml` to point to the new image file while updating the X/Y (set to 0) and updating the W/H (set as required).

## gitadora-textool.exe
Convert individual tex files.
```
usage: gitadora-textool.exe input_filename
```

If you specify a .tex file as the `input_filename` then the tool will convert the .tex to .png.

If you specify a non-.tex file as the `input_filename` then the tool will try to convert the image file to .tex.
The tool uses C#'s Bitmap class to load images, so any format supported normally by C# should work (PNG, JPG, BMP, etc).
PNG is the only "officially" supported format but JPG should be safe as well, and probably others too.
